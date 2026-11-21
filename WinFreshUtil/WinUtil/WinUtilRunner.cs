using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using WinFreshUtil.Services;

namespace WinFreshUtil.WinUtil
{
    /// <summary>
    /// The request that the page hands to the elevated helper. Only keys of the local
    /// definitions are accepted, so the file cannot be used to run an arbitrary command.
    /// </summary>
    public sealed class WinUtilRequest
    {
        public List<WinUtilAction> Actions { get; set; }
        public List<string> AppxKeys { get; set; }
        public string DnsProvider { get; set; }
        public string Created { get; set; }

        public WinUtilRequest()
        {
            Actions = new List<WinUtilAction>();
            AppxKeys = new List<string>();
            DnsProvider = string.Empty;
        }
    }

    /// <summary>
    /// Runs the local WinUtil work. The registry and service changes need administrator
    /// rights, so a second, elevated instance of this application does the work and the
    /// page follows its log.
    /// </summary>
    public static class WinUtilRunner
    {
        private const int WaitMinutes = 60;

        public static string GetFolder()
        {
            return Path.Combine(AppPaths.GetDataFolder(), "winutil");
        }

        public static string GetRequestPath()
        {
            return Path.Combine(GetFolder(), "request.json");
        }

        public static string GetLogPath()
        {
            return Path.Combine(GetFolder(), "run.log");
        }

        public static string GetResultPath()
        {
            return Path.Combine(GetFolder(), "result.txt");
        }

        /// <summary>
        /// Runs the selection and reports the lines while the work goes on. This method is
        /// meant for a background thread.
        /// </summary>
        public static WinUtilRunResult Run(WinUtilSelection selection, Action<WinUtilLogLine> onLine)
        {
            if (selection == null || selection.IsEmpty)
            {
                return new WinUtilRunResult { Success = true, Message = "Nothing was picked." };
            }

            if (AdminService.IsElevated())
            {
                // The application itself has the rights; no second instance is needed.
                return WinUtilEngine.Run(selection, onLine);
            }

            string error;
            string folder = GetFolder();

            try
            {
                Directory.CreateDirectory(folder);

                WinUtilRequest request = new WinUtilRequest
                {
                    Actions = selection.Actions,
                    AppxKeys = selection.AppxKeys,
                    DnsProvider = selection.DnsProvider ?? string.Empty,
                    Created = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                };

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                File.WriteAllText(GetRequestPath(), serializer.Serialize(request), new UTF8Encoding(false));

                DeleteIfPresent(GetLogPath());
                DeleteIfPresent(GetResultPath());
            }
            catch (Exception ex)
            {
                return new WinUtilRunResult
                {
                    Success = false,
                    Message = "The request could not be written. " + ex.Message
                };
            }

            if (!AdminService.RelaunchAsAdmin(StartupAction.WinUtil, out error))
            {
                return new WinUtilRunResult
                {
                    Success = false,
                    Message = "The changes need administrator rights. " + error
                };
            }

            return WaitForResult(onLine);
        }

        /// <summary>
        /// The work of the elevated instance. Reads the request, runs the local definitions
        /// and writes the result. It is called at startup instead of showing a window.
        /// </summary>
        public static int RunHelper()
        {
            string logPath = GetLogPath();
            string resultPath = GetResultPath();
            StreamWriter writer = null;
            object gate = new object();

            try
            {
                string folder = GetFolder();
                Directory.CreateDirectory(folder);

                writer = new StreamWriter(
                    new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
                    new UTF8Encoding(false));
                writer.AutoFlush = true;

                StreamWriter logWriter = writer;
                Action<WinUtilLogLine> onLine = delegate(WinUtilLogLine line)
                {
                    lock (gate)
                    {
                        logWriter.WriteLine(LevelCode(line.Level) + "|" + (line.Text ?? string.Empty).Replace("\r", " ").Replace("\n", " "));
                    }
                };

                WinUtilRequest request = ReadRequest();
                if (request == null)
                {
                    WriteResult(resultPath, false, 0, 0, "The request could not be read.");
                    return 1;
                }

                DateTime created;
                if (DateTime.TryParse(request.Created, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out created) &&
                    DateTime.UtcNow - created.ToUniversalTime() > TimeSpan.FromMinutes(10))
                {
                    WriteResult(resultPath, false, 0, 0, "The request is too old.");
                    return 1;
                }

                WinUtilSelection selection = new WinUtilSelection
                {
                    Actions = request.Actions ?? new List<WinUtilAction>(),
                    AppxKeys = request.AppxKeys ?? new List<string>(),
                    DnsProvider = request.DnsProvider ?? string.Empty
                };

                WinUtilRunResult result = WinUtilEngine.Run(selection, onLine);
                WriteResult(resultPath, result.Success, result.Completed, result.Failed, result.Message);
                return result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                try
                {
                    WriteResult(resultPath, false, 0, 0, ex.Message);
                }
                catch (Exception)
                {
                    // The caller reports the missing result.
                }

                return 1;
            }
            finally
            {
                if (writer != null)
                {
                    writer.Dispose();
                }

                DeleteIfPresent(GetRequestPath());
            }
        }

        private static WinUtilRequest ReadRequest()
        {
            string path = GetRequestPath();
            if (!File.Exists(path))
            {
                return null;
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            return serializer.Deserialize<WinUtilRequest>(File.ReadAllText(path, Encoding.UTF8));
        }

        private static WinUtilRunResult WaitForResult(Action<WinUtilLogLine> onLine)
        {
            string logPath = GetLogPath();
            string resultPath = GetResultPath();
            long position = 0;
            DateTime deadline = DateTime.UtcNow.AddMinutes(WaitMinutes);

            while (DateTime.UtcNow < deadline)
            {
                position = ReadLog(logPath, position, onLine);

                if (File.Exists(resultPath))
                {
                    try
                    {
                        string text = File.ReadAllText(resultPath, Encoding.UTF8).Trim();
                        string[] parts = text.Split('|');

                        if (parts.Length >= 5)
                        {
                            WinUtilRunResult result = new WinUtilRunResult
                            {
                                Success = string.Equals(parts[0], "OK", StringComparison.OrdinalIgnoreCase),
                                Completed = ParseInt(parts[1]),
                                Failed = ParseInt(parts[2]),
                                Message = parts[4]
                            };

                            DeleteIfPresent(resultPath);
                            DeleteIfPresent(GetRequestPath());
                            return result;
                        }
                    }
                    catch (IOException)
                    {
                        // The helper is still writing the file.
                    }
                }

                Thread.Sleep(200);
            }

            return new WinUtilRunResult
            {
                Success = false,
                Message = "The changes did not finish in time. The log has the last line that was written."
            };
        }

        private static long ReadLog(string path, long position, Action<WinUtilLogLine> onLine)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return position;
                }

                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (position > stream.Length)
                    {
                        position = 0;
                    }

                    stream.Position = position;

                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (onLine != null && line.Length > 1)
                            {
                                onLine(new WinUtilLogLine(line.Substring(2), ToLevel(line[0])));
                            }
                        }

                        position = stream.Position;
                    }
                }
            }
            catch (IOException)
            {
                // A line that is being written is read on the next pass.
            }

            return position;
        }

        private static void WriteResult(string path, bool success, int completed, int failed, string message)
        {
            string text = (success ? "OK" : "FAIL") + "|" +
                          completed.ToString(CultureInfo.InvariantCulture) + "|" +
                          failed.ToString(CultureInfo.InvariantCulture) + "|0|" +
                          (message ?? string.Empty).Replace("|", "/");

            string temporary = path + ".tmp";
            File.WriteAllText(temporary, text, new UTF8Encoding(false));

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }

        private static int ParseInt(string text)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        private static char LevelCode(WinUtilLogLevel level)
        {
            switch (level)
            {
                case WinUtilLogLevel.Success:
                    return 'S';
                case WinUtilLogLevel.Warning:
                    return 'W';
                case WinUtilLogLevel.Error:
                    return 'E';
                default:
                    return 'I';
            }
        }

        private static WinUtilLogLevel ToLevel(char code)
        {
            switch (code)
            {
                case 'S':
                    return WinUtilLogLevel.Success;
                case 'W':
                    return WinUtilLogLevel.Warning;
                case 'E':
                    return WinUtilLogLevel.Error;
                default:
                    return WinUtilLogLevel.Info;
            }
        }

        private static void DeleteIfPresent(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // A leftover file is overwritten by the next run.
            }
        }
    }
}
