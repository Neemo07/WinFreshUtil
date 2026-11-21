using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WinFreshUtil.Services.Driver
{
    /// <summary>
    /// The files that the application and the elevated instance use to talk to each
    /// other, and the place where the steps of the work are written down.
    /// </summary>
    public static class DriverFolder
    {
        private const string RequestFileName = "driver-request.txt";
        private const string ProgressFileName = "driver-progress.log";
        private const string ResultFileName = "driver-result.txt";
        private const string StopFileName = "driver-stop.txt";

        /// <summary>
        /// The file the elevated instance reads its work from.
        /// </summary>
        public static string GetRequestPath()
        {
            return Path.Combine(AppPaths.GetDataFolder(), RequestFileName);
        }

        /// <summary>
        /// The file the steps of the work are written to.
        /// </summary>
        public static string GetProgressPath()
        {
            return Path.Combine(AppPaths.GetDataFolder(), ProgressFileName);
        }

        /// <summary>
        /// The file the result of the work is written to.
        /// </summary>
        public static string GetResultPath()
        {
            return Path.Combine(AppPaths.GetDataFolder(), ResultFileName);
        }

        private static string GetStopPath()
        {
            return Path.Combine(AppPaths.GetDataFolder(), StopFileName);
        }

        /// <summary>
        /// Asks the elevated instance to stop. The application does not wait any
        /// longer for a result after this.
        /// </summary>
        public static void RequestStop()
        {
            try
            {
                File.WriteAllText(GetStopPath(), "stop", Encoding.ASCII);
                LogService.Info("The installation of the driver was asked to stop.");
            }
            catch (Exception)
            {
                // The file is only a sign for the elevated instance.
            }
        }

        /// <summary>
        /// Takes the sign to stop away, so that an earlier run does not stop a new one.
        /// </summary>
        public static void ClearStop()
        {
            try
            {
                if (File.Exists(GetStopPath()))
                {
                    File.Delete(GetStopPath());
                }
            }
            catch (Exception)
            {
                // The file is only a sign for the elevated instance.
            }
        }

        /// <summary>
        /// True when the user asked for the installation to stop.
        /// </summary>
        public static bool IsStopRequested()
        {
            try
            {
                return File.Exists(GetStopPath());
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Writes one step of the work, so that the application can show it.
        /// </summary>
        public static void WriteStep(string progressPath, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            LogService.Info(text);

            try
            {
                File.AppendAllText(progressPath, text + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // The step is still in the log of the application.
            }
        }

        /// <summary>
        /// Writes the result of the work, so that the application knows that it is
        /// finished.
        /// </summary>
        public static void WriteResult(bool success, string message)
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

        /// <summary>
        /// Reads the steps of the work and passes the new ones to the caller.
        ///
        /// The file is read from the place where the last reading stopped, and only
        /// the lines that are complete are read. A line that is still being written is
        /// read the next time, so that no step is left out and none is told twice.
        /// </summary>
        public static void ReportProgress(string progressPath, Action<string> progress, ref long offset)
        {
            byte[] buffer;
            long start = offset;

            try
            {
                using (FileStream stream = new FileStream(progressPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
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
                // There is no whole line yet.
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
    }
}
