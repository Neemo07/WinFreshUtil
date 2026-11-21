using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Runs the Microsoft Activation Scripts (MAS) from
    /// https://github.com/massgravel/Microsoft-Activation-Scripts
    ///
    /// The scripts are carried inside the application, so the activation itself needs
    /// no internet. The script is asked what it can do, the list is shown in the
    /// application and the function that the user picks is then run. Everything happens
    /// in a hidden elevated console, so no window is opened.
    /// </summary>
    public static class ActivationService
    {
        public const string RepositoryUrl = "https://github.com/massgravel/Microsoft-Activation-Scripts";

        private const string ScriptFileName = "MAS_AIO.cmd";
        private const string RequestFileName = "request.txt";
        private const string MarkerFileName = "helper.marker";
        private const string LogFileName = "output.log";
        private const string ResultFileName = "result.txt";
        private const string StopFileName = "stop.txt";

        /// <summary>
        /// The file the helper reads its work from. It has a fixed name because the
        /// elevated helper is started without a command line.
        /// </summary>
        public static string GetRequestPath()
        {
            return Path.Combine(GetWorkingFolder(), RequestFileName);
        }

        /// <summary>
        /// The file that tells an instance of the application that it was started to work
        /// through a request of the activation helper.
        /// </summary>
        public static string GetHelperMarkerPath()
        {
            return Path.Combine(AppPaths.GetDataFolder(), MarkerFileName);
        }

        /// <summary>
        /// <summary>
        /// The folder the scripts are kept in. It is deliberately not inside the
        /// temporary folder, because MAS refuses to run from there.
        /// </summary>
        public static string GetWorkingFolder()
        {
            return AppPaths.GetMasFolder();
        }

        /// <summary>
        /// The exact command line that starts a script. It is shown to the user before
        /// anything is downloaded or executed.
        /// </summary>
        public static string GetCommandLine(string scriptFile, string arguments)
        {
            string folder = GetWorkingFolder();
            string command = "cmd.exe /c \"\"" + Path.Combine(folder, scriptFile) + "\"";

            if (!string.IsNullOrEmpty(arguments))
            {
                command += " " + arguments;
            }

            return command + " -qedit\"";
        }

        /// <summary>
        /// Prepares the script and starts it so that it shows its menu. The menu is read
        /// from the output afterwards. Runs on a background thread.
        /// </summary>
        public static MasMenu GetFunctions(Action<string> onOutput)
        {
            string error;
            string scriptPath = EnsureScript(ScriptFileName, out error);
            if (scriptPath == null)
            {
                return new MasMenu { Error = "The script could not be prepared. " + error };
            }

            // The answer "0" leaves the menu again once it has been shown. The list is
            // read from the output as soon as it is there, so the script does not have
            // to be waited for.
            Run(scriptPath, string.Empty, new List<MasHelper.Answer>(), onOutput, delegate
            {
                // The menu is printed line by line, so the list is only complete when
                // the entry that leaves the script is in it.
                MasMenu menu = ParseMenu(Path.Combine(GetWorkingFolder(), LogFileName));

                foreach (MasOption option in menu.Options)
                {
                    if (option.IsExit)
                    {
                        return true;
                    }
                }

                return false;
            });

            MasMenu menu = ParseMenu(Path.Combine(GetWorkingFolder(), LogFileName));
            if (menu.Options.Count == 0 && string.IsNullOrEmpty(menu.Error))
            {
                menu.Error = "The list of functions could not be read from the script.";
            }

            return menu;
        }

        /// <summary>
        /// Reads the editions of Windows that this computer can be changed to. The script
        /// is started with the answer "0", so that it prints its list and stops again.
        /// Runs on a background thread.
        /// </summary>
        public static IList<MasTarget> GetWindowsEditions(Action<string> onOutput)
        {
            MasCommand command = MasFunctionMapper.GetCommand("Change Windows Edition");
            if (command == null)
            {
                return null;
            }

            string error;
            string scriptPath = EnsureScript(command.ScriptFile, out error);
            if (scriptPath == null)
            {
                onOutput("The script could not be prepared. " + error);
                return null;
            }

            List<MasHelper.Answer> answers = new List<MasHelper.Answer>();
            answers.Add(new MasHelper.Answer { Text = "0", NeedsEnter = true });

            Run(scriptPath, string.Empty, answers, onOutput, delegate
            {
                return ParseEditions(Path.Combine(GetWorkingFolder(), LogFileName)).Count > 0;
            });

            return ParseEditions(Path.Combine(GetWorkingFolder(), LogFileName));
        }

        /// <summary>
        /// Runs one of the functions of the script. Runs on a background thread.
        /// </summary>
        public static ActivationOutcome RunMasCommand(MasCommand command, MasTarget target, Action<string> onOutput)
        {
            if (command == null)
            {
                return new ActivationOutcome { Success = false, Message = "The function is not known." };
            }

            string error;
            string scriptPath = EnsureScript(command.ScriptFile, out error);
            if (scriptPath == null)
            {
                return new ActivationOutcome
                {
                    Success = false,
                    Message = "The script could not be prepared. " + error
                };
            }

            List<MasHelper.Answer> answers = new List<MasHelper.Answer>();

            if (target != null && target.Answers != null)
            {
                foreach (string answer in target.Answers)
                {
                    answers.Add(new MasHelper.Answer { Text = answer, NeedsEnter = false });
                }
            }

            if (command.Answers != null)
            {
                foreach (string answer in command.Answers)
                {
                    answers.Add(new MasHelper.Answer { Text = answer, NeedsEnter = false });
                }
            }

            // The safe answer for every further question of the script.
            for (int i = 0; i < 8; i++)
            {
                answers.Add(new MasHelper.Answer { Text = "0", NeedsEnter = false });
            }

            return Run(scriptPath, command.Arguments, answers, onOutput, null);
        }

        /// <summary>
        /// Runs a script that is already on the computer. Runs on a background thread.
        /// </summary>
        public static ActivationOutcome RunScriptFile(string scriptPath, Action<string> onOutput)
        {
            return Run(scriptPath, string.Empty, new List<MasHelper.Answer>(), onOutput, null);
        }

        /// <summary>
        /// Starts a script in an elevated helper and waits until it has finished.
        /// </summary>
        private static ActivationOutcome Run(string scriptPath, string arguments, List<MasHelper.Answer> answers,
            Action<string> onOutput, Func<bool> isFinished)
        {
            string folder = GetWorkingFolder();
            string logPath = Path.Combine(folder, LogFileName);
            string requestPath = GetRequestPath();
            string resultPath = Path.Combine(folder, ResultFileName);

            MasHelper.Request request = new MasHelper.Request
            {
                ScriptPath = scriptPath,
                Arguments = arguments ?? string.Empty,
                LogPath = logPath,
                ResultPath = resultPath,
                StopPath = Path.Combine(folder, StopFileName),
                Answers = answers
            };

            try
            {
                MasHelper.WriteRequest(requestPath, request);

                if (File.Exists(logPath))
                {
                    File.Delete(logPath);
                }

                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }

                // A stop file of an earlier run would end the script straight away.
                string stopPath = Path.Combine(folder, StopFileName);
                if (File.Exists(stopPath))
                {
                    File.Delete(stopPath);
                }
            }
            catch (Exception ex)
            {
                return new ActivationOutcome
                {
                    Success = false,
                    Message = "The request could not be written. " + ex.Message
                };
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(
                    GetApplicationPath(),
                    "\"" + requestPath + "\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                // The elevated instance does not get the command line, so a marker file
                // tells it that it was started to work through the request.
                StartupAction.Set(StartupAction.MasHelper);

                System.Diagnostics.Process.Start(startInfo);

                int exitCode = WaitForResult(resultPath, logPath, onOutput, isFinished);
                if (exitCode == StoppedByCaller)
                {
                    return new ActivationOutcome
                    {
                        Success = true,
                        Message = "The list was read."
                    };
                }

                if (exitCode == int.MinValue)
                {
                    return new ActivationOutcome
                    {
                        Success = false,
                        Message = "The script did not finish in time."
                    };
                }

                return new ActivationOutcome
                {
                    Success = exitCode == 0,
                    ExitCode = exitCode,
                    Message = exitCode == 0
                        ? "The function has finished."
                        : "The function ended with exit code " + exitCode + "."
                };
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)
                {
                    return new ActivationOutcome
                    {
                        Success = false,
                        Message = "The elevation request was declined, so nothing was run."
                    };
                }

                return new ActivationOutcome
                {
                    Success = false,
                    Message = "The script could not be started. " + ex.Message
                };
            }
            catch (Exception ex)
            {
                return new ActivationOutcome
                {
                    Success = false,
                    Message = "The script could not be started. " + ex.Message
                };
            }
        }

        /// <summary>
        /// Returned by the waiting when the caller stopped the script itself.
        /// </summary>
        private const int StoppedByCaller = -2;

        /// <summary>
        /// Waits for the helper to write its result and returns the exit code of the
        /// script, or int.MinValue when the helper did not finish in time.
        /// </summary>
        private static int WaitForResult(string resultPath, string logPath, Action<string> onOutput, Func<bool> isFinished)
        {
            DateTime deadline = DateTime.Now.AddMinutes(10);

            while (DateTime.Now < deadline)
            {
                Report(logPath, onOutput);

                if (isFinished != null && isFinished())
                {
                    // Ask the helper to stop the script, so that nothing is left running.
                    try
                    {
                        File.WriteAllText(Path.Combine(GetWorkingFolder(), StopFileName), "stop");
                    }
                    catch (Exception)
                    {
                        // The helper stops the script by itself when its time is up.
                    }

                    return StoppedByCaller;
                }

                try
                {
                    if (File.Exists(resultPath))
                    {
                        string content = File.ReadAllText(resultPath).Trim();
                        int exitCode;
                        if (content.Length > 0 && int.TryParse(content, out exitCode))
                        {
                            Report(logPath, onOutput);
                            return exitCode;
                        }
                    }
                }
                catch (Exception)
                {
                    // The result file is still being written.
                }

                System.Threading.Thread.Sleep(300);
            }

            return int.MinValue;
        }

        /// <summary>
        /// Reports the current content of the log file.
        /// </summary>
        private static void Report(string logPath, Action<string> onOutput)
        {
            if (onOutput == null)
            {
                return;
            }

            string content;
            try
            {
                using (FileStream stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.Default))
                {
                    content = reader.ReadToEnd();
                }
            }
            catch (Exception)
            {
                return;
            }

            if (content.Length > 0)
            {
                onOutput(content);
            }
        }

        /// <summary>
        /// Reads the entries of the MAS menu out of the output of the script.
        /// </summary>
        private static MasMenu ParseMenu(string logPath)
        {
            MasMenu menu = new MasMenu();

            string content = ReadLog(logPath);
            if (content == null)
            {
                menu.Error = "The output of the script could not be read.";
                return menu;
            }

            // The entries look like "  [1] HWID                - Windows".
            Regex pattern = new Regex(@"^\s*\[([0-9A-Za-z])\]\s{1,}(.+?)\s*$", RegexOptions.Multiline);
            foreach (Match match in pattern.Matches(content))
            {
                string key = match.Groups[1].Value;
                string label = match.Groups[2].Value.Trim();

                if (label.Length == 0)
                {
                    continue;
                }

                string name = label;
                string description = string.Empty;

                int split = label.IndexOf(" - ", StringComparison.Ordinal);
                if (split > 0)
                {
                    name = label.Substring(0, split).Trim();
                    description = label.Substring(split + 3).Trim();
                }

                bool isExit = key == "0" || name.Equals("Exit", StringComparison.OrdinalIgnoreCase);

                bool known = false;
                foreach (MasOption option in menu.Options)
                {
                    if (option.Key == key)
                    {
                        known = true;
                        break;
                    }
                }

                if (known)
                {
                    continue;
                }

                menu.Options.Add(new MasOption
                {
                    Key = key,
                    Label = name,
                    Description = description,
                    IsExit = isExit
                });
            }

            return menu;
        }

        /// <summary>
        /// Reads the editions that the script offers. The script asks for the number of
        /// the edition, so the number is kept with the name.
        /// </summary>
        private static IList<MasTarget> ParseEditions(string logPath)
        {
            List<MasTarget> editions = new List<MasTarget>();

            string content = ReadLog(logPath);
            if (content == null)
            {
                return editions;
            }

            Regex pattern = new Regex(@"^\s*\[(\d+)\]\s{1,}(\S+)\s*$", RegexOptions.Multiline);
            foreach (Match match in pattern.Matches(content))
            {
                string number = match.Groups[1].Value;
                string name = match.Groups[2].Value.Trim();

                if (name.Length == 0)
                {
                    continue;
                }

                bool known = false;
                foreach (MasTarget edition in editions)
                {
                    if (edition.Label == name)
                    {
                        known = true;
                        break;
                    }
                }

                if (known)
                {
                    continue;
                }

                editions.Add(new MasTarget
                {
                    Label = name,
                    Description = string.Empty,

                    // The edition is chosen by its number, and the number is read from
                    // the keyboard of the console.
                    Answers = new[] { number }
                });
            }

            return editions;
        }

        private static string ReadLog(string logPath)
        {
            try
            {
                return StripAnsi(File.ReadAllText(logPath));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Removes the colour codes that MAS writes to the console.
        /// </summary>
        private static string StripAnsi(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return Regex.Replace(text, "\x1b\\[[0-9;]*[A-Za-z]", string.Empty);
        }

        /// <summary>
        /// Makes sure the script is on the computer. The scripts are carried inside the
        /// application, so nothing is downloaded. Writing the bytes verbatim keeps the
        /// line endings and the trailing empty line that the batch scripts require.
        ///
        /// A script that was updated from the project is used while it still has the
        /// hash of the update. A missing or changed file falls back to the bundled copy.
        /// </summary>
        private static string EnsureScript(string fileName, out string error)
        {
            error = null;

            try
            {
                string folder = GetWorkingFolder();
                Directory.CreateDirectory(folder);

                string target = Path.Combine(folder, fileName);

                if (File.Exists(target) && ScriptHash.IsExpected(target))
                {
                    return target;
                }

                // The file is missing or was changed, so an update that was made earlier
                // no longer applies and the copy inside the application is used again.
                MasUpdateService.Invalidate();

                string expected = ScriptHash.BundledHashFor(fileName);

                if (!PayloadService.TryRestore(fileName, target, expected, out error))
                {
                    LogService.Error(error);
                    return null;
                }

                // The hash of the script is checked again before it is used. A script
                // that is not the one this application knows is not used.
                if (!ScriptHash.IsExpected(target))
                {
                    string hash = ScriptHash.OfFile(target);

                    error = "The script on this computer is not the one this application knows. " +
                            "Its hash is " + (hash ?? "unknown") + " and it should be " + expected + ".";

                    LogService.Error("The activation script was refused. " + error);
                    return null;
                }

                return target;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                LogService.Error("The script " + fileName + " could not be put in place.", ex);
                return null;
            }
        }

        private static string GetApplicationPath()
        {
            try
            {
                return System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            }
            catch (Exception)
            {
                return System.Reflection.Assembly.GetEntryAssembly().Location;
            }
        }
    }
}
