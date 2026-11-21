using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// The small things that the work needs: starting a program with a list of
    /// arguments, getting a file and the place of a program of Windows.
    /// </summary>
    public static class DriverTools
    {
        private const string Agent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";

        /// <summary>
        /// Runs a program with the given arguments and waits for it.
        ///
        /// The arguments are handed over as a list and are put in the right form for
        /// Windows, so that a value with a space or a quote in it stays one argument
        /// and is never read as a second command. Nothing is handed over to a command
        /// interpreter, which is what makes it safe to run this with administrator
        /// rights.
        /// </summary>
        public static string RunProgram(string program, string[] arguments, int timeout, out int exitCode)
        {
            return RunProgram(program, arguments, timeout, out exitCode, null);
        }

        /// <summary>
        /// Runs a program with the given arguments and waits for it. Every line that
        /// the program prints is given to <paramref name="onLine"/> while it runs, so
        /// that the caller can show how far it is.
        /// </summary>
        public static string RunProgram(string program, string[] arguments, int timeout, out int exitCode,
            Action<string> onLine)
        {
            exitCode = -1;

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

                List<string> lines = new List<string>();

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return string.Empty;
                    }

                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                        {
                            AddLine(lines, e.Data, onLine);
                        }
                    };

                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                        {
                            AddLine(lines, e.Data, onLine);
                        }
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(timeout))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception)
                        {
                            // The process has already ended.
                        }

                        lock (lines)
                        {
                            lines.Add("The command did not finish in time.");
                        }

                        return JoinLines(lines);
                    }

                    // The lines that are still on the way are read first.
                    process.WaitForExit();

                    exitCode = process.ExitCode;

                    return JoinLines(lines);
                }
            }
            catch (Exception ex)
            {
                return "The command could not be run. " + ex.Message;
            }
        }

        private static string JoinLines(List<string> lines)
        {
            lock (lines)
            {
                return string.Join(Environment.NewLine, lines.ToArray()).Trim();
            }
        }

        /// <summary>
        /// Keeps a line of the output and tells the caller about it.
        /// </summary>
        private static void AddLine(List<string> lines, string line, Action<string> onLine)
        {
            lock (lines)
            {
                lines.Add(line);
            }

            if (onLine != null)
            {
                onLine(line);
            }
        }

        /// <summary>
        /// The place of a program of Windows, so that it is not looked for in the
        /// folder the application was started from.
        /// </summary>
        public static string GetSystemProgram(string name)
        {
            try
            {
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

                if (!string.IsNullOrEmpty(windows))
                {
                    string path = Path.Combine(windows, "System32", name);

                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch (Exception)
            {
                // The name is used then.
            }

            return name;
        }

        /// <summary>
        /// Reads a page into a text.
        /// </summary>
        public static string DownloadString(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = Agent;
            request.Timeout = 60000;
            request.Headers.Add(HttpRequestHeader.CacheControl, "no-cache");
            request.Headers.Add(HttpRequestHeader.Pragma, "no-cache");

            using (WebResponse response = request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Downloads a file.
        /// </summary>
        public static void DownloadFile(string url, string path)
        {
            using (WebClient client = new WebClient())
            {
                client.Headers.Add(HttpRequestHeader.UserAgent, Agent);
                client.DownloadFile(url, path);
            }
        }
    }
}
