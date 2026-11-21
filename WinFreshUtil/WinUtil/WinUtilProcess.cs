using System;
using System.Diagnostics;
using System.Text;

namespace WinFreshUtil.WinUtil
{
    /// <summary>
    /// Runs a local program with a hidden window and reports its output line by line.
    /// </summary>
    public static class WinUtilProcess
    {
        private const int DefaultTimeoutMilliseconds = 30 * 60 * 1000;

        /// <summary>
        /// Runs a local program. The output is passed to the caller as it arrives.
        /// </summary>
        public static int Run(
            string fileName,
            string arguments,
            Action<string> onLine,
            out string error,
            int timeoutMilliseconds = DefaultTimeoutMilliseconds)
        {
            error = null;

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                    {
                        if (args.Data != null && onLine != null)
                        {
                            onLine(args.Data);
                        }
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                    {
                        if (args.Data != null && onLine != null)
                        {
                            onLine(args.Data);
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception)
                        {
                            // The process may already have ended.
                        }

                        error = "The program " + fileName + " did not finish in time.";
                        return -1;
                    }

                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                error = "The program " + fileName + " could not be started. " + ex.Message;
                return -1;
            }
        }

        /// <summary>
        /// Runs a PowerShell script file that is on the computer. The script is local; no
        /// web cmdlet is used by the scripts that this application writes.
        /// </summary>
        public static int RunPowerShell(
            string scriptPath,
            Action<string> onLine,
            out string error,
            int timeoutMilliseconds = DefaultTimeoutMilliseconds)
        {
            string arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(scriptPath);
            return Run("powershell.exe", arguments, onLine, out error, timeoutMilliseconds);
        }

        /// <summary>
        /// Puts a value in quotes for a Windows command line.
        /// </summary>
        public static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            StringBuilder result = new StringBuilder();
            result.Append('"');
            int backslashes = 0;

            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    result.Append(character);
                    continue;
                }

                if (character == '"')
                {
                    result.Append('\\', backslashes + 1);
                    result.Append('"');
                    backslashes = 0;
                    continue;
                }

                backslashes = 0;
                result.Append(character);
            }

            result.Append('\\', backslashes);
            result.Append('"');
            return result.ToString();
        }
    }
}
