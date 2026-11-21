using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Writes the log of the application to a file in the data folder of the user.
    /// Writing a log line never throws, so a full disk or a missing folder can never
    /// stop the application.
    /// </summary>
    public static class LogService
    {
        private const int MaxLength = 512 * 1024;
        private static readonly object Gate = new object();

        /// <summary>
        /// The file the log is written to.
        /// </summary>
        public static string GetLogPath()
        {
            return Path.Combine(AppPaths.GetDataFolder(), "log.txt");
        }

        public static void Info(string message)
        {
            Write("INFO ", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN ", message, null);
        }

        public static void Error(string message)
        {
            Write("ERROR", message, null);
        }

        public static void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        /// <summary>
        /// Writes a line to the log file.
        /// </summary>
        public static void Write(string level, string message, Exception exception)
        {
            try
            {
                string folder = AppPaths.GetDataFolder();
                Directory.CreateDirectory(folder);

                StringBuilder line = new StringBuilder();
                line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                line.Append("  ").Append(level).Append("  ").Append(message);

                if (exception != null)
                {
                    line.AppendLine();
                    line.Append("        ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);

                    if (exception.StackTrace != null)
                    {
                        foreach (string stackLine in exception.StackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            line.AppendLine();
                            line.Append("        ").Append(stackLine.Trim());
                        }
                    }
                }

                lock (Gate)
                {
                    File.AppendAllText(GetLogPath(), line.ToString() + Environment.NewLine, Encoding.UTF8);
                    TrimIfNeeded();
                }
            }
            catch (Exception)
            {
                // A log line must never stop the application.
            }
        }

        /// <summary>
        /// Keeps the log from growing without end.
        /// </summary>
        private static void TrimIfNeeded()
        {
            try
            {
                string path = GetLogPath();
                FileInfo file = new FileInfo(path);
                if (!file.Exists || file.Length < MaxLength)
                {
                    return;
                }

                // Keep the second half of the file, so the oldest lines are dropped.
                string content = File.ReadAllText(path);
                int start = content.Length / 2;
                int end = content.IndexOf('\n', start);
                if (end < 0)
                {
                    end = content.Length - 1;
                }

                File.WriteAllText(path, content.Substring(end + 1), Encoding.UTF8);
            }
            catch (Exception)
            {
                // Trimming is not important enough to report.
            }
        }
    }
}
