using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Runs a MAS script in a hidden console and types the answers into that console.
    ///
    /// MAS calls the "mode" command before it asks the user to pick something. That
    /// command replaces a redirected standard input with the console itself, so the
    /// answers cannot be piped into the script. They have to be typed into the console,
    /// which is what this helper does.
    ///
    /// The helper runs as a second, elevated instance of this application so that it has
    /// the same rights as the script it drives.
    /// </summary>
    public static class MasHelper
    {
        public const string RunArgument = "--mas-run";

        private const int KeyEvent = 1;
        private const uint KeyUnicode = 0x0004;
        private const ushort VirtualKeyReturn = 0x0D;
        private const int StdInputHandle = -10;
        private const int StdOutputHandle = -11;
        private const int SwHide = 0;

        /// <summary>
        /// How long the answer waits before it is typed, so that the script has finished
        /// setting up the way it reads the keyboard.
        /// </summary>
        private static readonly TimeSpan AnswerDelay = TimeSpan.FromSeconds(2);

        /// <summary>
        /// The longest time a script may run before it is stopped.
        /// </summary>
        private static readonly TimeSpan MaxRunTime = TimeSpan.FromMinutes(4);


        [StructLayout(LayoutKind.Sequential)]
        private struct ShortPoint
        {
            public short X;
            public short Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ScreenBufferInfo
        {
            public ShortPoint Size;
            public ShortPoint CursorPosition;
            public short Attributes;
            public ShortPoint Window;
            public ShortPoint MaximumWindowSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct InputRecord
        {
            public ushort EventType;
            public KeyboardInput KeyDown;
            public uint EventFlags;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int stdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleScreenBufferInfo(IntPtr handle, out ScreenBufferInfo info);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool ReadConsoleOutputCharacter(
            IntPtr handle,
            StringBuilder buffer,
            uint length,
            ShortPoint coordinates,
            out uint read);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteConsoleInput(
            IntPtr handle,
            InputRecord[] buffer,
            uint length,
            out uint written);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr handle, int command);

        /// <summary>
        /// One answer that is typed into the console.
        /// </summary>
        public sealed class Answer
        {
            public string Text { get; set; }

            /// <summary>True when the answer has to be confirmed with the Enter key.</summary>
            public bool NeedsEnter { get; set; }
        }

        /// <summary>
        /// The work the helper has to do.
        /// </summary>
        public sealed class Request
        {
            public string ScriptPath { get; set; }
            public string Arguments { get; set; }
            public string LogPath { get; set; }
            public string ResultPath { get; set; }

            /// <summary>
            /// The file that tells the helper to stop the script.
            /// </summary>
            public string StopPath { get; set; }
            public List<Answer> Answers { get; set; }
        }

        /// <summary>
        /// Runs the request and returns the exit code of the script.
        /// </summary>
        public static int Run(string requestPath)
        {
            WriteStatus("helper started");

            Request request = ReadRequest(requestPath);
            if (request == null)
            {
                WriteStatus("the request could not be read");
                return -1;
            }

            bool ownsConsole = AllocConsole();
            WriteStatus("console allocated: " + ownsConsole);

            if (ownsConsole)
            {
                ShowWindow(GetConsoleWindow(), SwHide);
            }

            try
            {
                return RunScript(request);
            }
            catch (Exception ex)
            {
                WriteStatus("error: " + ex.Message);
                return -1;
            }
            finally
            {
                if (ownsConsole)
                {
                    FreeConsole();
                }
            }
        }

        private static int RunScript(Request request)
        {
            // A script is only run when it still has the hash that the application
            // knows. The file could have been changed between the moment it was put in
            // place and this moment.
            string expected = ScriptHash.ExpectedFor(request.ScriptPath);

            if (expected != null)
            {
                string hash = ScriptHash.OfFile(request.ScriptPath);

                if (!string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
                {
                    WriteStatus("the script was refused, its hash is " + (hash ?? "unknown") +
                                " and it should be " + expected);

                    WriteResult(request.ResultPath, -1);
                    return -1;
                }
            }

            // The doubled outer quotes are the form cmd.exe expects: it strips the first
            // and the last quote and then runs the quoted path.
            string command = "/c \"\"" + request.ScriptPath + "\"";

            if (!string.IsNullOrEmpty(request.Arguments))
            {
                command += " " + request.Arguments;
            }

            // The script keeps running in this console so that its prompts can be answered.
            if (!NeedsArchitectureRelaunch())
            {
                // MAS otherwise restarts itself in a second console window to turn off
                // QuickEdit, which would break the answer queue.
                command += " -qedit";
            }

            string commandLine = command + "\"";
            string shimFolder = CreateShim(Path.GetDirectoryName(request.ScriptPath));
            string queuePath = Path.Combine(Path.GetDirectoryName(request.ScriptPath), "queue.txt");
            string counterPath = Path.Combine(Path.GetDirectoryName(request.ScriptPath), "counter.txt");

            WriteQueue(queuePath, request.Answers);
            if (File.Exists(counterPath))
            {
                File.Delete(counterPath);
            }
            ProcessStartInfo startInfo = new ProcessStartInfo("cmd.exe", commandLine)
            {
                UseShellExecute = false,

                // The script inherits the console of this helper, so the answers that are
                // typed into that console are read by the script. Its output stays on the
                // console as well, because the script clears the screen while it runs,
                // which would throw a redirected output away.
                CreateNoWindow = false
            };

            // The replacement of the choice command has to be found first.
            startInfo.EnvironmentVariables["PATH"] = shimFolder + ";" + startInfo.EnvironmentVariables["PATH"];
            startInfo.EnvironmentVariables["WFU_QUEUE"] = queuePath;
            startInfo.EnvironmentVariables["WFU_COUNTER"] = counterPath;

            IntPtr input = GetStdHandle(StdInputHandle);
            IntPtr output = GetStdHandle(StdOutputHandle);

            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return -1;
                }

                WriteStatus("script started, pid " + process.Id);
                DateTime started = DateTime.Now;

                // The answers are typed when MAS asks for them. They cannot be typed
                // right at the start, because MAS runs the "mode" command before it asks
                // for a selection, and that command empties the console input buffer.
                // Answers that are confirmed with the enter key are read from the
                // keyboard of the console, the others are handed to the replacement
                // of the choice command.
                List<Answer> enterAnswers = new List<Answer>();
                if (request.Answers != null)
                {
                    foreach (Answer answer in request.Answers)
                    {
                        if (answer != null && answer.NeedsEnter)
                        {
                            enterAnswers.Add(answer);
                        }
                    }
                }

                int nextAnswer = 0;
                string lastContent = string.Empty;
                Answer pending = null;
                DateTime pendingSince = DateTime.Now;

                while (!process.HasExited)
                {
                    string content = ReadConsoleText(output, request.LogPath);

                    if (!string.IsNullOrEmpty(content) && content != lastContent)
                    {
                        bool newPrompt = HasNewPrompt(lastContent, content);

                        lastContent = content;

                        if (newPrompt && pending == null && nextAnswer < enterAnswers.Count)
                        {
                            pending = enterAnswers[nextAnswer];
                            nextAnswer++;
                            pendingSince = DateTime.Now;
                        }
                    }

                    // The answer is not typed straight away. MAS prints the question and
                    // then starts reading the key, and changing the way the console reads
                    // the keyboard empties the input buffer. A short wait makes sure the
                    // question is really waiting for the answer.
                    if (pending != null && DateTime.Now - pendingSince > AnswerDelay)
                    {
                        WriteStatus("typing answer: " + pending.Text);
                        TypeAnswer(input, pending);
                        pending = null;
                    }

                    if (IsStopRequested(request.StopPath))
                    {
                        LogService.Info("The activation script is stopped by the application.");
                        StopProcess(process);
                        break;
                    }

                    if (DateTime.Now - started > MaxRunTime)
                    {
                        LogService.Warn("The activation script did not finish in time and is stopped.");
                        StopProcess(process);
                        break;
                    }

                    System.Threading.Thread.Sleep(200);
                }

                process.WaitForExit();
                ReadConsoleText(output, request.LogPath);

                WriteStatus("script exit code: " + process.ExitCode);
                WriteResult(request.ResultPath, process.ExitCode);
                return process.ExitCode;
            }
        }


        /// <summary>
        /// The folder that holds the replacement for the choice command.
        /// </summary>
        private const string ShimFolderName = "shim";

        /// <summary>
        /// The batch file that replaces the choice command of Windows.
        ///
        /// The choice command reads a key from the keyboard, and the console throws
        /// away everything that was typed into it when it changes the way it reads the
        /// keyboard. A replacement that takes the answer from a file is used instead,
        /// so that the menus of the script can be answered.
        /// </summary>
        private const string ChoiceShim =
            "@echo off\r\n" +
            "powershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0choice-shim.ps1\" %*\r\n" +
            "exit /b %errorlevel%\r\n";

        /// <summary>
        /// The script behind the replacement of the choice command.
        /// </summary>
        private const string ChoiceShimScript =
            "$chars = ''\r\n" +
            "foreach ($a in $args) { if ($a -like '/C:*') { $chars = $a.Substring(3) } }\r\n" +
            "$queue = @()\r\n" +
            "if (Test-Path $env:WFU_QUEUE) { $queue = @(Get-Content $env:WFU_QUEUE -ErrorAction SilentlyContinue) }\r\n" +
            "$count = 0\r\n" +
            "if (Test-Path $env:WFU_COUNTER) { $text = (Get-Content $env:WFU_COUNTER -ErrorAction SilentlyContinue | Select-Object -First 1); if ($text) { $count = [int]$text } }\r\n" +
            "$answer = '0'\r\n" +
            "if ($count -lt $queue.Count) { $answer = $queue[$count] }\r\n" +
            "Set-Content $env:WFU_COUNTER ($count + 1)\r\n" +
            "$index = 0\r\n" +
            "if ($answer.Length -gt 0) { $found = $chars.IndexOf($answer[0]); if ($found -ge 0) { $index = $found + 1 } }\r\n" +
            "if ($index -eq 0) { $found = $chars.IndexOf('0'); if ($found -ge 0) { $index = $found + 1 } }\r\n" +
            "if ($index -eq 0) { $index = 1 }\r\n" +
            "Write-Output $answer\r\n" +
            "exit $index\r\n";

        /// <summary>
        /// Writes the replacement for the choice command and returns its folder.
        /// </summary>
        private static string CreateShim(string folder)
        {
            string shimFolder = Path.Combine(folder, ShimFolderName);
            Directory.CreateDirectory(shimFolder);

            File.WriteAllText(Path.Combine(shimFolder, "choice.cmd"), ChoiceShim, Encoding.ASCII);
            File.WriteAllText(Path.Combine(shimFolder, "choice-shim.ps1"), ChoiceShimScript, Encoding.ASCII);

            return shimFolder;
        }

        /// <summary>
        /// Writes the answers that the replacement of the choice command hands out.
        /// </summary>
        private static void WriteQueue(string queuePath, List<Answer> answers)
        {
            List<string> lines = new List<string>();

            if (answers != null)
            {
                foreach (Answer answer in answers)
                {
                    // Answers that are confirmed with the enter key are read from the
                    // keyboard of the console, the others go to the choice command.
                    if (answer != null && !answer.NeedsEnter && !string.IsNullOrEmpty(answer.Text))
                    {
                        lines.Add(answer.Text);
                    }
                }
            }

            File.WriteAllLines(queuePath, lines.ToArray(), Encoding.ASCII);
        }

        /// <summary>
        /// True when the application asked the script to stop.
        /// </summary>
        private static bool IsStopRequested(string stopPath)
        {
            if (string.IsNullOrEmpty(stopPath))
            {
                return false;
            }

            try
            {
                return File.Exists(stopPath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Stops the script, so that no console window is left behind.
        /// </summary>
        private static void StopProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception)
            {
                // The script has already ended.
            }
        }

        /// <summary>
        /// Writes the exit code to the result file. The application waits for this file
        /// because it cannot watch the helper process itself.
        /// </summary>
        private static void WriteResult(string resultPath, int exitCode)
        {
            if (string.IsNullOrEmpty(resultPath))
            {
                return;
            }

            try
            {
                File.WriteAllText(resultPath, exitCode.ToString(), Encoding.ASCII);
            }
            catch (Exception)
            {
                // The result file is only used to signal that the run has finished.
            }
        }

        /// <summary>
        /// The texts MAS uses when it waits for a selection.
        /// </summary>
        private static readonly string[] PromptMarkers =
        {
            "Enter an option number",
            "Choose a menu option",
            "Continue Anyway",
            "Activate Anyway",
            "Press [0] key",
            "Press any key",
            "Go Back",
            "Open Support Webpage"
        };

        /// <summary>
        /// True when the new output contains a prompt that was not in the output before.
        /// </summary>
        private static bool HasNewPrompt(string previous, string current)
        {
            foreach (string marker in PromptMarkers)
            {
                if (current.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    previous.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Types one answer into the console input buffer.
        /// </summary>
        private static void TypeAnswer(IntPtr input, Answer answer)
        {
            if (answer == null || string.IsNullOrEmpty(answer.Text))
            {
                return;
            }

            if (input == IntPtr.Zero || input == new IntPtr(-1))
            {
                WriteStatus("no console input handle");
                return;
            }

            List<InputRecord> records = new List<InputRecord>();

            foreach (char character in answer.Text)
            {
                records.Add(CreateCharacter(character));
            }

            if (answer.NeedsEnter)
            {
                records.Add(CreateKey(VirtualKeyReturn));
            }

            if (records.Count == 0)
            {
                return;
            }

            uint written;
            WriteConsoleInput(input, records.ToArray(), (uint)records.Count, out written);
        }

        /// <summary>
        /// Reads the console buffer and writes it to the log file. The text that was read
        /// is returned as well.
        /// </summary>
        private static string ReadConsoleText(IntPtr output, string logPath)
        {
            if (output == IntPtr.Zero || output == new IntPtr(-1))
            {
                return string.Empty;
            }

            ScreenBufferInfo info;
            if (!GetConsoleScreenBufferInfo(output, out info))
            {
                return string.Empty;
            }

            int width = info.Size.X;
            int height = info.Size.Y;

            if (width <= 0 || height <= 0)
            {
                return string.Empty;
            }

            string text = ReadConsoleBuffer(output, width, height);

            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            try
            {
                // The file is written first under another name and then moved over the
                // old one, so that the application never reads a half written file.
                string temporary = logPath + ".tmp";
                File.WriteAllText(temporary, text, Encoding.Default);

                if (File.Exists(logPath))
                {
                    File.Delete(logPath);
                }

                File.Move(temporary, logPath);
            }
            catch (Exception ex)
            {
                WriteStatus("writing the log failed: " + ex.Message);
            }

            return text;
        }

        /// <summary>
        /// Reads the console buffer and writes it to the log file.
        /// </summary>
        private static void WriteConsoleSnapshot(IntPtr output, string logPath)
        {
            ReadConsoleText(output, logPath);
        }

        /// <summary>
        /// Reads the whole console buffer, including the part that scrolled out of view.
        /// The buffer is read in small blocks because a single call for the whole buffer
        /// returns nothing on a console with a large scrollback.
        /// </summary>
        private static string ReadConsoleBuffer(IntPtr output, int width, int height)
        {
            const int linesPerBlock = 50;

            StringBuilder result = new StringBuilder();

            for (int start = 0; start < height; start += linesPerBlock)
            {
                int count = Math.Min(linesPerBlock, height - start);

                StringBuilder buffer = new StringBuilder(width * count);
                uint read;
                ShortPoint origin;
                origin.X = 0;
                origin.Y = (short)start;

                if (!ReadConsoleOutputCharacter(output, buffer, (uint)(width * count), origin, out read))
                {
                    WriteStatus("ReadConsoleOutputCharacter failed at " + start + ": " + Marshal.GetLastWin32Error());
                    break;
                }


                string raw = buffer.ToString();
                for (int y = 0; y < count; y++)
                {
                    int offset = y * width;
                    if (offset + width > raw.Length)
                    {
                        break;
                    }

                    string line = raw.Substring(offset, width).TrimEnd();
                    if (line.Length > 0)
                    {
                        result.AppendLine(line);
                    }
                }
            }

            return StripAnsi(result.ToString());
        }

        /// <summary>
        /// Types the answers into the console input buffer. The console keeps them queued
        /// until the script asks for them.
        /// </summary>
        private static void TypeAnswers(IntPtr input, List<Answer> answers)
        {
            if (answers == null)
            {
                return;
            }

            if (input == IntPtr.Zero || input == new IntPtr(-1))
            {
                WriteStatus("no console input handle");
                return;
            }

            foreach (Answer answer in answers)
            {
                if (answer == null || string.IsNullOrEmpty(answer.Text))
                {
                    continue;
                }

                List<InputRecord> records = new List<InputRecord>();

                foreach (char character in answer.Text)
                {
                    records.Add(CreateCharacter(character));
                }

                if (answer.NeedsEnter)
                {
                    records.Add(CreateKey(VirtualKeyReturn));
                }

                if (records.Count == 0)
                {
                    continue;
                }

                uint written;
                WriteConsoleInput(input, records.ToArray(), (uint)records.Count, out written);
            }
        }

        private static InputRecord CreateCharacter(char character)
        {
            InputRecord record = new InputRecord();
            record.EventType = KeyEvent;

            // A real key press is sent as a virtual key, so that the console turns it
            // into a character the same way it does for a person at the keyboard.
            record.KeyDown.VirtualKey = (ushort)char.ToUpperInvariant(character);
            record.KeyDown.ScanCode = 0;
            record.KeyDown.Flags = 0;
            record.KeyDown.Time = 0;
            record.KeyDown.ExtraInfo = IntPtr.Zero;
            record.EventFlags = 0;
            return record;
        }

        private static InputRecord CreateKey(ushort virtualKey)
        {
            InputRecord record = new InputRecord();
            record.EventType = KeyEvent;
            record.KeyDown.VirtualKey = virtualKey;
            record.KeyDown.ScanCode = 0;
            record.KeyDown.Flags = 0;
            record.KeyDown.Time = 0;
            record.KeyDown.ExtraInfo = IntPtr.Zero;
            record.EventFlags = 0;
            return record;
        }

        /// <summary>
        /// True when MAS would restart itself in a different process for architecture
        /// reasons. In that case MAS has to do that restart anyway and the -qedit flag
        /// must not be used, because it would also suppress the architecture restart.
        /// </summary>
        private static bool NeedsArchitectureRelaunch()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(windows))
            {
                return false;
            }

            // Sysnative only exists for a 32-bit process on a 64-bit system.
            if (File.Exists(Path.Combine(windows, "Sysnative", "cmd.exe")))
            {
                return true;
            }

            // SysArm32 only exists on ARM64 systems running emulated processes.
            return File.Exists(Path.Combine(windows, "SysArm32", "cmd.exe"));
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

            return System.Text.RegularExpressions.Regex.Replace(text, "\x1b\\[[0-9;]*[A-Za-z]", string.Empty);
        }

        private static Request ReadRequest(string requestPath)
        {
            try
            {
                Request request = new Request { Answers = new List<Answer>() };

                Dictionary<string, string> values = KeyValueFile.Read(requestPath);

                foreach (KeyValuePair<string, string> entry in values)
                {
                    string value = entry.Value;

                    switch (entry.Key)
                    {
                        case "S":
                            request.ScriptPath = value;
                            break;
                        case "A":
                            request.Arguments = value;
                            break;
                        case "L":
                            request.LogPath = value;
                            break;
                        case "R":
                            request.ResultPath = value;
                            break;
                        case "P":
                            request.StopPath = value;
                            break;
                        case "N":
                            // N=text;1  or  N=text;0
                            int separator = value.LastIndexOf(';');
                            bool needsEnter = separator > 0 && value.Substring(separator + 1) == "1";
                            request.Answers.Add(new Answer
                            {
                                Text = separator > 0 ? value.Substring(0, separator) : value,
                                NeedsEnter = needsEnter
                            });
                            break;
                    }
                }

                if (string.IsNullOrEmpty(request.ScriptPath) || string.IsNullOrEmpty(request.LogPath))
                {
                    return null;
                }

                return request;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Writes a request file that the helper reads.
        /// </summary>
        public static void WriteRequest(string requestPath, Request request)
        {
            List<KeyValuePair<string, string>> values = KeyValueFile.Values();

            KeyValueFile.Add(values, "S", request.ScriptPath);
            KeyValueFile.Add(values, "A", request.Arguments);
            KeyValueFile.Add(values, "L", request.LogPath);
            KeyValueFile.Add(values, "R", request.ResultPath);
            KeyValueFile.Add(values, "P", request.StopPath);

            foreach (Answer answer in request.Answers)
            {
                KeyValueFile.Add(values, "N", answer.Text + ";" + (answer.NeedsEnter ? '1' : '0'));
            }

            KeyValueFile.Write(requestPath, values);
        }

        /// <summary>
        /// Writes a diagnostic line to the log of the application.
        /// </summary>
        public static void WriteStatus(string text)
        {
            LogService.Info(text);
        }
        }
}
