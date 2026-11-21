using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Searches for programs with the package manager of Windows and installs them.
    /// The programs come from the sources that the user has set up, so nothing is
    /// downloaded from an unknown place.
    /// </summary>
    public static class AppInstallService
    {
        private const string Winget = "winget.exe";
        private const string Choco = "choco.exe";

        /// <summary>
        /// The state of one installation.
        /// </summary>
        public sealed class InstallProgress
        {
            /// <summary>
            /// How far the program is, in percent. It is -1 when the package manager
            /// does not say how far it is, which happens when its output is read
            /// instead of shown on a console.
            /// </summary>
            public int Percent { get; set; }

            /// <summary>The last line that the package manager printed.</summary>
            public string Message { get; set; }
        }

        /// <summary>
        #region Searching

        /// <summary>
        /// Searches for programs. Runs on a background thread.
        /// </summary>
        public static IList<AppPackage> Search(string query)
        {
            List<AppPackage> results = new List<AppPackage>();

            string winget = PackageManagerLocator.FindWinget();
            if (winget == null || string.IsNullOrEmpty(query))
            {
                return results;
            }

            try
            {
                string output = Run(winget, new[] { "search", "--query", query.Replace("\"", ""), "--accept-source-agreements" }, 180000, null);
                results.AddRange(WingetTable.Parse(output));
            }
            catch (Exception)
            {
                // The caller shows that nothing was found.
            }

            return results;
        }

        /// <summary>
        /// Installs a program that was found with the search. Runs on a background
        /// thread.
        /// </summary>
        public static string Install(AppPackage package)
        {
            return Install(package, null);
        }

        /// <summary>
        /// Installs a program that was found with the search and reports the progress.
        /// Runs on a background thread.
        /// </summary>
        public static string Install(AppPackage package, Action<InstallProgress> onProgress)
        {
            if (package == null || string.IsNullOrEmpty(package.Id))
            {
                return "No program was picked.";
            }

            return InstallById(package.Id, false, onProgress);
        }

        #endregion

        #region Installing from the list

        /// <summary>
        /// Installs a program by its name in the package manager and reports the
        /// progress. Runs on a background thread.
        /// </summary>
        public static string InstallById(string id, bool useChocolatey, Action<InstallProgress> onProgress)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "No program was picked.";
            }

            if (useChocolatey)
            {
                string choco = PackageManagerLocator.FindChoco();
                if (choco == null)
                {
                    return "Chocolatey is not on this computer.";
                }

                return Run(choco, BuildInstallArguments(id, useChocolatey), 900000, onProgress);
            }

            string winget = PackageManagerLocator.FindWinget();
            if (winget == null)
            {
                return "The package manager of Windows is not on this computer.";
            }

            return Run(winget, BuildInstallArguments(id, useChocolatey), 900000, onProgress);
        }

        /// <summary>
        /// Removes a program. Runs on a background thread.
        /// </summary>
        public static string UninstallById(string id, bool useChocolatey, Action<InstallProgress> onProgress)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "No program was picked.";
            }

            if (useChocolatey)
            {
                string choco = PackageManagerLocator.FindChoco();
                if (choco == null)
                {
                    return "Chocolatey is not on this computer.";
                }

                return Run(choco, BuildUninstallArguments(id, useChocolatey), 900000, onProgress);
            }

            string winget = PackageManagerLocator.FindWinget();
            if (winget == null)
            {
                return "The package manager of Windows is not on this computer.";
            }

            return Run(winget, BuildUninstallArguments(id, useChocolatey), 900000, onProgress);
        }

        /// <summary>
        /// Brings every program that has a newer version up to date and reports the
        /// progress. Runs on a background thread.
        /// </summary>
        public static string UpgradeAll(bool useChocolatey, Action<InstallProgress> onProgress)
        {
            if (useChocolatey)
            {
                string choco = PackageManagerLocator.FindChoco();
                if (choco == null)
                {
                    return "Chocolatey is not on this computer.";
                }

                return Run(choco, new[] { "upgrade", "all", "-y", "--no-progress", "--limit-output" }, 1800000, onProgress);
            }

            string winget = PackageManagerLocator.FindWinget();
            if (winget == null)
            {
                return "The package manager of Windows is not on this computer.";
            }

            return Run(winget,
                new[] { "upgrade", "--all", "--silent", "--accept-package-agreements",
                        "--accept-source-agreements", "--disable-interactivity" },
                1800000, onProgress);
        }

        /// <summary>
        /// The programs that are on the computer. The list is only asked for once and
        /// then kept, so that the page does not wait for it every time it is opened.
        /// With <paramref name="refresh"/> the list is asked for again.
        /// </summary>
        public static ISet<string> GetInstalledIdsCached(bool useChocolatey, bool refresh)
        {
            lock (InstalledGate)
            {
                if (!refresh)
                {
                    if (useChocolatey && _chocoInstalled != null)
                    {
                        return _chocoInstalled;
                    }

                    if (!useChocolatey && _wingetInstalled != null)
                    {
                        return _wingetInstalled;
                    }
                }
            }

            ISet<string> ids = GetInstalledIds(useChocolatey);

            lock (InstalledGate)
            {
                if (useChocolatey)
                {
                    _chocoInstalled = ids;
                }
                else
                {
                    _wingetInstalled = ids;
                }
            }

            return ids;
        }

        private static readonly object InstalledGate = new object();
        private static ISet<string> _wingetInstalled;
        private static ISet<string> _chocoInstalled;

        /// <summary>
        /// The names of the programs that are on the computer. The names of the
        /// programs are in the set as well, because many programs are written down
        /// with the name that Windows has for them and not with the name of the
        /// package manager. Runs on a background thread.
        /// </summary>
        public static ISet<string> GetInstalledIds(bool useChocolatey)
        {
            HashSet<string> installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                if (useChocolatey)
                {
                    string choco = PackageManagerLocator.FindChoco();
                    if (choco == null)
                    {
                        return installed;
                    }

                    string output = Run(choco, new[] { "list", "--local-only", "--limit-output" }, 300000, null);

                    foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] parts = line.Split('|');
                        if (parts.Length > 0 && parts[0].Trim().Length > 0)
                        {
                            installed.Add(parts[0].Trim());
                        }
                    }

                    return installed;
                }

                string winget = PackageManagerLocator.FindWinget();
                if (winget == null)
                {
                    return installed;
                }

                string list = Run(winget, new[] { "list", "--accept-source-agreements" }, 300000, null);

                foreach (AppPackage package in WingetTable.Parse(list))
                {
                    if (!string.IsNullOrEmpty(package.Id))
                    {
                        installed.Add(package.Id);
                    }

                    if (!string.IsNullOrEmpty(package.Name))
                    {
                        installed.Add(package.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error("The list of installed programs could not be read.", ex);
            }

            LogService.Info("The list of the installed programs was read in " +
                watch.ElapsedMilliseconds + " ms, " + installed.Count + " name(s).");

            return installed;
        }

        #endregion


        /// <summary>
        /// The arguments that install a program. The same list is used for the program
        /// itself and for the text that is shown before it runs, so that both are
        /// always the same.
        /// </summary>
        public static string[] BuildInstallArguments(string id, bool useChocolatey)
        {
            if (useChocolatey)
            {
                return new[] { "install", id, "-y", "--no-progress", "--limit-output" };
            }

            return new[]
            {
                "install", "--id", id, "--exact", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"
            };
        }

        /// <summary>
        /// The arguments that remove a program.
        ///
        /// The remove command of the package manager of Windows does not know the
        /// switch for the package agreements, so it must not be given.
        /// </summary>
        public static string[] BuildUninstallArguments(string id, bool useChocolatey)
        {
            if (useChocolatey)
            {
                return new[] { "uninstall", id, "-y", "--no-progress", "--limit-output" };
            }

            return new[]
            {
                "uninstall", "--id", id, "--exact", "--silent",
                "--accept-source-agreements", "--disable-interactivity"
            };
        }

        /// <summary>
        /// The exact command that is used for a program. It is shown to the user
        /// before anything is installed.
        /// </summary>
        public static string GetInstallCommand(string id, bool useChocolatey)
        {
            return GetProgramName(useChocolatey) + " " +
                   ProcessArguments.Join(BuildInstallArguments(id, useChocolatey));
        }

        /// <summary>
        /// The exact command that is used to remove a program.
        /// </summary>
        public static string GetUninstallCommand(string id, bool useChocolatey)
        {
            return GetProgramName(useChocolatey) + " " +
                   ProcessArguments.Join(BuildUninstallArguments(id, useChocolatey));
        }

        private static string GetProgramName(bool useChocolatey)
        {
            return useChocolatey ? "choco" : "winget";
        }

        /// <summary>
        /// Runs a program and waits for it. The lines that the program prints are read
        /// while it runs, so that the caller can show how far it is. Runs on a
        /// background thread.
        /// </summary>
        public static string Run(string program, string[] arguments, int timeout, Action<InstallProgress> onProgress)
        {
            List<string> lines = new List<string>();

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(program)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                startInfo.Arguments = ProcessArguments.Join(arguments);

                // The package managers write their text in UTF-8, also when the output
                // is read instead of shown.
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;

                LogService.Info("The program is started: " + program + " " + startInfo.Arguments);

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return string.Empty;
                    }

                    SetRunning(process);

                    // The progress of the package managers is separated by a carriage
                    // return instead of a line feed. The readers split on both, so a
                    // progress line is not held back until the next line arrives.
                    System.Threading.Thread outputReader = StartReader(process.StandardOutput, lines, onProgress);
                    System.Threading.Thread errorReader = StartReader(process.StandardError, lines, onProgress);

                    if (!process.WaitForExit(timeout))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception)
                        {
                            // The program has already ended.
                        }

                        AddLine(lines, "The program did not finish in time.", onProgress);
                    }
                    else
                    {
                        // The lines that are still on the way are read first.
                        process.WaitForExit();
                    }

                    outputReader.Join(5000);
                    errorReader.Join(5000);

                    lock (lines)
                    {
                        return string.Join(Environment.NewLine, lines.ToArray()).Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                return "The program could not be run. " + ex.Message;
            }
            finally
            {
                SetRunning(null);
            }
        }

        /// <summary>
        /// The program that is running at the moment, so that it can be stopped when
        /// the application is closed.
        /// </summary>
        private static readonly object RunningGate = new object();
        private static Process _running;

        private static void SetRunning(Process process)
        {
            lock (RunningGate)
            {
                _running = process;
            }
        }

        /// <summary>
        /// Stops the program that is running at the moment. It is used when the
        /// application is closed, so that an installation does not go on without the
        /// user seeing it.
        /// </summary>
        public static void KillRunning()
        {
            try
            {
                Process process;

                lock (RunningGate)
                {
                    process = _running;
                    _running = null;
                }

                if (process != null && !process.HasExited)
                {
                    process.Kill();
                    LogService.Info("The program " + process.ProcessName + " was stopped because the application was closed.");
                }
            }
            catch (Exception)
            {
                // The program has already ended.
            }
        }

        /// <summary>
        /// Reads how far a program is out of the line it printed.
        /// </summary>
        public static int ReadPercent(string line)
        {
            return InstallOutput.ReadPercent(line);
        }

        /// <summary>
        /// Reads one output stream of the program on a thread of its own. The text is
        /// passed on at every carriage return and line feed, so the progress lines of
        /// the package managers arrive while the program still runs.
        /// </summary>
        private static System.Threading.Thread StartReader(
            StreamReader reader,
            List<string> lines,
            Action<InstallProgress> onProgress)
        {
            System.Threading.Thread thread = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                StringBuilder part = new StringBuilder();

                try
                {
                    while (true)
                    {
                        int value = reader.Read();
                        if (value < 0)
                        {
                            break;
                        }

                        char character = (char)value;
                        if (character == '\r' || character == '\n')
                        {
                            FlushPart(lines, part, onProgress);
                            continue;
                        }

                        part.Append(character);
                    }
                }
                catch (Exception)
                {
                    // The stream is closed when the program ends.
                }

                FlushPart(lines, part, onProgress);
            }));

            thread.IsBackground = true;
            thread.Start();
            return thread;
        }

        private static void FlushPart(List<string> lines, StringBuilder part, Action<InstallProgress> onProgress)
        {
            if (part.Length == 0)
            {
                return;
            }

            string line = part.ToString().TrimEnd();
            part.Length = 0;

            if (line.Length > 0)
            {
                AddLine(lines, line, onProgress);
            }
        }

        /// <summary>
        /// Keeps a line of the output and tells the caller about it.
        /// </summary>
        private static void AddLine(List<string> lines, string line, Action<InstallProgress> onProgress)
        {
            lock (lines)
            {
                lines.Add(line);
            }

            if (onProgress == null)
            {
                return;
            }

            InstallProgress progress = new InstallProgress
            {
                Percent = InstallOutput.ReadPercent(line),
                Message = line
            };

            onProgress(progress);
        }

    }
}
