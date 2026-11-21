using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// The RapidOS modification of Windows.
    ///
    /// The sources of the project are taken from a fixed commit, so that a change of
    /// the project does not change what is applied to the computer without the user
    /// seeing it. The hash of the archive is checked, and the work itself is done by
    /// an instance of the application that has administrator rights.
    ///
    /// The project is at https://github.com/rapid-community/RapidOS/ and is made for
    /// Windows 10 22H2 and Windows 11 22H2 up to 25H2.
    /// </summary>
    public static class RapidOsService
    {
        /// <summary>
        /// The commit of the project that the sources come from.
        /// </summary>
        public const string Commit = "9c8ce87e1c9bc2b095a1dbff3e073bdf47dd47e5";

        /// <summary>
        /// The version of RapidOS that this commit holds.
        /// </summary>
        public const string Version = "1.3";

        /// <summary>
        /// The hash of the archive of that commit. The archive of a commit is always
        /// the same, so the hash can be written down.
        /// </summary>
        public const string ArchiveSha256 = "62C5BCF5192F50B3D73D7D7D3D53687ED1D70EB561B172E2FD0DFB44A7015357";

        /// <summary>
        /// The builds of Windows that the project supports.
        /// </summary>
        private static readonly string[] SupportedBuilds = { "19045", "22621", "22631", "26100", "26200" };

        private const string RequestFileName = "rapidos-request.txt";
        private const string LogFileName = "rapidos-output.log";
        private const string ResultFileName = "rapidos-result.txt";

        /// <summary>
        /// The script that starts the project. It is written next to the sources.
        /// </summary>
        private const string WrapperScript = "Run-RapidOs.ps1";

        private const string WrapperText =
            "# The script that starts RapidOS.\r\n" +
            "# The modules of the project are loaded first, they hold the functions that\r\n" +
            "# the playbooks use.\r\n" +
            "# The text of the scripts is sent to the output, so that the person sees what\r\n" +
            "# is happening while the work runs.\r\n" +
            "$root = Split-Path -Parent $MyInvocation.MyCommand.Path\r\n" +
            "$setup = Join-Path $root 'RapidScripts\\EnvSetup.ps1'\r\n" +
            "if (Test-Path $setup) { & $setup }\r\n" +
            "& (Join-Path $root 'Template.ps1') 6>&1\r\n";

        /// <summary>
        /// The address the archive is downloaded from.
        /// </summary>
        public static string ArchiveUrl
        {
            get { return "https://github.com/rapid-community/RapidOS/archive/" + Commit + ".zip"; }
        }

        /// <summary>
        /// The folder the sources are put in.
        /// </summary>
        public static string GetFolder()
        {
            return Path.Combine(AppPaths.GetDataFolder(), "rapidos");
        }

        /// <summary>
        /// The folder with the sources of the project.
        /// </summary>
        public static string GetSourcesPath()
        {
            return Path.Combine(GetFolder(), "RapidOS-" + Commit, "RapidOS Sources");
        }

        /// <summary>
        /// The folder with the scripts that the project runs.
        /// </summary>
        public static string GetExecutablesPath()
        {
            return Path.Combine(GetSourcesPath(), "Executables");
        }

        /// <summary>
        /// The file the elevated instance reads its work from.
        /// </summary>
        public static string GetRequestPath()
        {
            return Path.Combine(GetFolder(), RequestFileName);
        }

        /// <summary>
        /// The file the output of the work is written to.
        /// </summary>
        public static string GetLogPath()
        {
            return Path.Combine(GetFolder(), LogFileName);
        }

        /// <summary>
        /// The file the result of the work is written to.
        /// </summary>
        public static string GetResultPath()
        {
            return Path.Combine(GetFolder(), ResultFileName);
        }

        /// <summary>
        /// The build of Windows that is on the computer, for example 22631.
        ///
        /// The number is read from the registry, because the version that the system
        /// tells is the old one when the program has no manifest of its own.
        /// </summary>
        public static string GetBuild()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("CurrentBuildNumber");

                        if (value != null)
                        {
                            return Convert.ToString(value);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // The number of the system is used then.
            }

            try
            {
                return Environment.OSVersion.Version.Build.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// True when the build of Windows is one that the project supports.
        /// </summary>
        public static bool IsSupportedBuild(out string build)
        {
            build = GetBuild();

            foreach (string supported in SupportedBuilds)
            {
                if (string.Equals(supported, build, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The exact command that is used. It is shown to the user before anything is
        /// downloaded or run.
        /// </summary>
        public static string GetInstallCommand()
        {
            return "powershell -NoProfile -ExecutionPolicy Bypass -File \"" +
                   Path.Combine(GetExecutablesPath(), WrapperScript) + "\"";
        }

        /// <summary>
        /// The place the sources come from, for the window.
        /// </summary>
        public static string GetSourceText()
        {
            return "https://github.com/rapid-community/RapidOS/ (the commit " +
                   Commit.Substring(0, 7) + ")";
        }

        /// <summary>
        /// Takes the sources out of the project and puts them on the computer. Runs on
        /// a background thread.
        /// </summary>
        public static void Download(Action<string> progress)
        {
            string folder = GetFolder();

            Report(progress, "The folder for the sources is made...");
            Directory.CreateDirectory(folder);

            string archive = Path.Combine(folder, "RapidOS-" + Commit + ".zip");

            Report(progress, "The sources of RapidOS " + Version + " are taken from this application...");

            string archiveName = Path.GetFileName(archive);
            string restoreError;

            // The archive is carried inside the application. A file that is already
            // there is used only when it still has the expected hash.
            if (!PayloadService.TryRestore(archiveName, archive, ArchiveSha256, out restoreError))
            {
                throw new InvalidOperationException(restoreError);
            }

            string hash = ScriptHash.OfFile(archive);

            if (!string.Equals(hash, ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The archive of RapidOS is not the one this application knows. Its hash is " +
                    (hash ?? "unknown") + " and it should be " + ArchiveSha256 + ".");
            }

            Report(progress, "The archive is the one of the commit " + Commit.Substring(0, 7) + ".");

            string target = Path.Combine(folder, "RapidOS-" + Commit);

            if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }

            Report(progress, "The sources are unpacked...");
            ZipFile.ExtractToDirectory(archive, folder);

            File.Delete(archive);

            string wrapper = Path.Combine(GetExecutablesPath(), WrapperScript);
            File.WriteAllText(wrapper, WrapperText, new UTF8Encoding(false));

            Report(progress, "The sources of RapidOS are ready.");
        }

        /// <summary>
        /// Applies RapidOS in an instance that has administrator rights and waits
        /// until it has finished. Runs on a background thread.
        /// </summary>
        public static OperationResult Install(Action<string> progress)
        {
            OperationResult result = new OperationResult();

            string requestPath = GetRequestPath();
            string logPath = GetLogPath();
            string resultPath = GetResultPath();

            try
            {
                List<KeyValuePair<string, string>> values = KeyValueFile.Values();
                KeyValueFile.Add(values, "F", GetSourcesPath());
                KeyValueFile.Write(requestPath, values);

                if (File.Exists(logPath))
                {
                    File.Delete(logPath);
                }

                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }
            }
            catch (Exception ex)
            {
                result.Message = "The request could not be written. " + ex.Message;
                return result;
            }

            string error;
            if (!AdminService.RelaunchAsAdmin(StartupAction.RapidOs, out error))
            {
                result.Message = string.IsNullOrEmpty(error)
                    ? "RapidOS could not be started with administrator rights."
                    : error;

                return result;
            }

            DateTime deadline = DateTime.Now.AddMinutes(60);
            long offset = 0;

            while (DateTime.Now < deadline)
            {
                ReadLog(logPath, progress, ref offset);

                try
                {
                    if (File.Exists(resultPath))
                    {
                        string content = File.ReadAllText(resultPath).Trim();

                        if (content.Length > 0)
                        {
                            ReadLog(logPath, progress, ref offset);

                            string[] parts = content.Split('|');
                            result.Success = parts.Length > 0 && parts[0] == "ok";
                            result.Message = parts.Length > 1
                                ? parts[1]
                                : (result.Success ? "RapidOS was applied." : "RapidOS could not be applied.");

                            return result;
                        }
                    }
                }
                catch (Exception)
                {
                    // The result file is still being written.
                }

                System.Threading.Thread.Sleep(500);
            }

            result.Success = false;
            result.Message = "The work did not finish in time.";
            return result;
        }

        /// <summary>
        /// Does the work of the elevated instance. Returns the exit code.
        /// </summary>
        public static int Run(string requestPath)
        {
            try
            {
                if (!AdminService.IsElevated())
                {
                    WriteResult(false, "The helper of RapidOS has to run with administrator rights.");
                    return -1;
                }

                Dictionary<string, string> request = KeyValueFile.Read(requestPath);

                string sources;
                request.TryGetValue("F", out sources);

                if (string.IsNullOrEmpty(sources) || !Directory.Exists(sources))
                {
                    WriteResult(false, "The sources of RapidOS were not found.");
                    return -1;
                }

                string executables = Path.Combine(sources, "Executables");
                string wrapper = Path.Combine(executables, WrapperScript);

                if (!File.Exists(wrapper))
                {
                    WriteResult(false, "The script that starts RapidOS was not found.");
                    return -1;
                }

                // The project expects its helper scripts in the folder of Windows.
                string rapidScripts = Path.Combine(sources, "Executables", "RapidScripts");
                string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "RapidScripts");

                try
                {
                    if (Directory.Exists(rapidScripts) && !Directory.Exists(target))
                    {
                        CopyDirectory(rapidScripts, target);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error("The helper scripts of RapidOS could not be put in place.", ex);
                }

                string command = "powershell -NoProfile -ExecutionPolicy Bypass -File \"" + wrapper + "\"";

                int exitCode;

                // The file with the output is made right away, so that it is there even
                // when the project has nothing to do.
                WriteLog("The work of RapidOS " + Version + " is started.");

                // Every line of the work is written down while it runs, so that the
                // person sees what is happening. The text at the end is not written
                // again, it is the same text.
                DriverTools.RunProgram("powershell.exe", new[]
                {
                    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", wrapper
                }, 3600000, out exitCode, delegate(string line)
                {
                    WriteLog(line);
                });

                bool success = exitCode == 0;

                WriteResult(success, success
                    ? "RapidOS was applied."
                    : "RapidOS ended with the code " + exitCode + ".");

                return success ? 0 : 1;
            }
            catch (Exception ex)
            {
                LogService.Error("The work of RapidOS failed.", ex);
                WriteResult(false, "RapidOS could not be applied. " + ex.Message);
                return -1;
            }
        }

        /// <summary>
        /// Copies a folder with all its files.
        /// </summary>
        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);

            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            }

            foreach (string folder in Directory.GetDirectories(source))
            {
                CopyDirectory(folder, Path.Combine(target, Path.GetFileName(folder)));
            }
        }

        /// <summary>
        /// Reads the output of the work and passes the new lines to the caller.
        /// </summary>
        private static void ReadLog(string logPath, Action<string> progress, ref long offset)
        {
            byte[] buffer;
            long start = offset;

            try
            {
                using (FileStream stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length <= start)
                    {
                        return;
                    }

                    stream.Seek(start, SeekOrigin.Begin);

                    using (MemoryStream copy = new MemoryStream())
                    {
                        stream.CopyTo(copy);
                        buffer = copy.ToArray();
                    }
                }
            }
            catch (Exception)
            {
                return;
            }

            int last = -1;

            for (int index = buffer.Length - 1; index >= 0; index--)
            {
                if (buffer[index] == (byte)'\n')
                {
                    last = index;
                    break;
                }
            }

            if (last < 0)
            {
                offset = start;
                return;
            }

            offset = start + last + 1;

            string text = Encoding.UTF8.GetString(buffer, 0, last + 1);

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();

                if (line.Length > 0 && progress != null)
                {
                    progress(line);
                }
            }
        }

        private static void WriteLog(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                File.AppendAllText(GetLogPath(), text + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // The output is still in the log of the application.
            }
        }

        private static void WriteResult(bool success, string message)
        {
            try
            {
                File.WriteAllText(
                    GetResultPath(),
                    (success ? "ok" : "error") + "|" + (message ?? string.Empty),
                    new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // The file is only used to tell the application that the work is done.
            }
        }

        private static void Report(Action<string> progress, string text)
        {
            if (progress != null && !string.IsNullOrEmpty(text))
            {
                progress(text);
            }
        }
    }
}
