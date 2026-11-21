using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Updates the activation scripts from
    /// https://github.com/massgravel/Microsoft-Activation-Scripts
    ///
    /// The application carries the scripts inside itself, so the activation needs no
    /// internet. This service is the only part of the activation section that uses the
    /// internet, and it only runs when the user asks for an update. The scripts that
    /// arrive are written next to the bundled copies and their hashes are recorded in
    /// a manifest, so the check before a run knows them as well.
    /// </summary>
    public static class MasUpdateService
    {
        public const string ManifestFileName = "mas.manifest";

        /// <summary>
        /// The addresses of the project. The commit is read first, so that every file
        /// of one update comes from the same state of the project.
        /// </summary>
        private const string CommitApiUrl =
            "https://api.github.com/repos/massgravel/Microsoft-Activation-Scripts/commits" +
            "?path=MAS/All-In-One-Version-KL/MAS_AIO.cmd&per_page=1";

        private const string RawBaseUrl =
            "https://raw.githubusercontent.com/massgravel/Microsoft-Activation-Scripts/";

        private const string UserAgent = "WinFreshUtil";

        /// <summary>The files that an update replaces.</summary>
        private static readonly string[] ScriptNames =
        {
            ScriptHash.MasFileName,
            "Check_Activation_Status.cmd",
            "Change_Windows_Edition.cmd",
            "Change_Office_Edition.cmd"
        };

        /// <summary>
        /// The switches that the functions of the page pass to the all in one script.
        /// An update that no longer knows one of them is refused, so a function can
        /// never run with a switch that means something else.
        /// </summary>
        private static readonly string[] RequiredSwitches =
        {
            "/HWID",
            "/Ohook",
            "/Z-WindowsESUOffice",
            "/K-WindowsOffice"
        };

        /// <summary>
        /// A script below this size is not a whole script. The all in one script is
        /// much larger than the separate ones.
        /// </summary>
        private const int MinimumScriptSize = 200;
        private const int MinimumAllInOneSize = 100000;

        private const int DownloadTimeoutMilliseconds = 60000;

        /// <summary>
        /// Downloads the latest scripts and records them as the ones to run. The
        /// scripts that are already on the computer stay in place when anything fails.
        /// Runs on a background thread.
        /// </summary>
        public static MasUpdateOutcome Update(Action<string> onOutput)
        {
            try
            {
                string folder = AppPaths.GetMasFolder();
                Directory.CreateDirectory(folder);

                Report(onOutput, "Reading the latest commit of the project...");

                string commit = GetLatestCommit();
                if (string.IsNullOrEmpty(commit))
                {
                    return Failed("The latest commit of the project could not be read.");
                }

                Report(onOutput, "Downloading the scripts of commit " + ShortCommit(commit) + "...");

                Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in ScriptNames)
                {
                    byte[] content = Download(RawBaseUrl + commit + "/" + GetRepositoryPath(name));

                    string error;
                    if (!IsWholeScript(name, content, out error))
                    {
                        return Failed(error);
                    }

                    files.Add(name, content);
                }

                // An update is a set of files. The manifest is removed first, so that a
                // failure while the files are replaced leaves no half update behind
                // that the application would trust.
                DeleteManifest();

                foreach (KeyValuePair<string, byte[]> file in files)
                {
                    WriteFile(Path.Combine(folder, file.Key), file.Value);
                }

                WriteManifest(folder, commit, files);

                return new MasUpdateOutcome
                {
                    Success = true,
                    Commit = commit,
                    Message = "The activation scripts were updated from GitHub (commit " +
                              ShortCommit(commit) + ")."
                };
            }
            catch (WebException ex)
            {
                LogService.Error("The activation scripts could not be downloaded.", ex);
                return Failed("The scripts could not be downloaded. " + ex.Message);
            }
            catch (Exception ex)
            {
                LogService.Error("The activation scripts could not be updated.", ex);
                return Failed("The scripts could not be updated. " + ex.Message);
            }
        }

        /// <summary>
        /// The hash that the manifest records for a script, or false when the script is
        /// not part of an update or lies outside the folder of the activation scripts.
        /// </summary>
        public static bool TryGetHash(string scriptPath, string fileName, out string hash)
        {
            hash = null;

            if (string.IsNullOrEmpty(scriptPath) || string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            if (!IsInMasFolder(scriptPath))
            {
                return false;
            }

            Dictionary<string, string> manifest = ReadManifest();
            string value;

            if (!manifest.TryGetValue(fileName, out value) || !IsHash(value))
            {
                return false;
            }

            hash = value;
            return true;
        }

        /// <summary>
        /// The commit of the scripts that were last updated from the project, or null
        /// when the bundled scripts are the ones that are run.
        /// </summary>
        public static string GetInstalledCommit()
        {
            Dictionary<string, string> manifest = ReadManifest();
            string commit;

            if (manifest.TryGetValue("Commit", out commit) && IsCommit(commit))
            {
                return commit;
            }

            return null;
        }

        /// <summary>
        /// The date of the last update, or null when there was none.
        /// </summary>
        public static string GetInstalledDate()
        {
            Dictionary<string, string> manifest = ReadManifest();
            string date;

            if (manifest.TryGetValue("Updated", out date) && !string.IsNullOrEmpty(date))
            {
                return date;
            }

            return null;
        }

        /// <summary>
        /// The version of the activation script, read from the script that would run.
        /// </summary>
        public static string GetScriptVersion()
        {
            string path = Path.Combine(AppPaths.GetMasFolder(), ScriptHash.MasFileName);
            string text = null;

            try
            {
                if (File.Exists(path) && ScriptHash.IsExpected(path))
                {
                    text = File.ReadAllText(path);
                }
            }
            catch (Exception)
            {
                // The bundled copy below is used instead.
            }

            if (text == null)
            {
                text = ReadBundledScript();
            }

            if (text == null)
            {
                return null;
            }

            Match match = Regex.Match(text, @"@set masver=([0-9A-Za-z._-]+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// A line for the page that tells which scripts would run.
        /// </summary>
        public static string GetVersionText()
        {
            string version = GetScriptVersion();
            string label = string.IsNullOrEmpty(version) ? "MAS" : "MAS " + version;
            string commit = GetInstalledCommit();

            if (!string.IsNullOrEmpty(commit) &&
                ScriptHash.IsExpected(Path.Combine(AppPaths.GetMasFolder(), ScriptHash.MasFileName)))
            {
                string date = GetInstalledDate();
                string when = string.IsNullOrEmpty(date) ? string.Empty : ", " + date;

                return label + ", updated from GitHub (commit " + ShortCommit(commit) + when + ").";
            }

            return label + ", bundled with this application. Activation runs without internet.";
        }

        /// <summary>
        /// Forgets the scripts of an update, so that the bundled copies are used again.
        /// </summary>
        public static void Invalidate()
        {
            DeleteManifest();
        }

        private static string GetLatestCommit()
        {
            string json = Encoding.UTF8.GetString(Download(CommitApiUrl));
            Match match = Regex.Match(json, "\"sha\"\\s*:\\s*\"([0-9a-fA-F]{40})\"");

            return match.Success ? match.Groups[1].Value : null;
        }

        private static string GetRepositoryPath(string fileName)
        {
            if (string.Equals(fileName, ScriptHash.MasFileName, StringComparison.OrdinalIgnoreCase))
            {
                return "MAS/All-In-One-Version-KL/" + fileName;
            }

            return "MAS/Separate-Files-Version/" + fileName;
        }

        private static byte[] Download(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;
            request.Accept = "application/vnd.github+json";
            request.Timeout = DownloadTimeoutMilliseconds;
            request.ReadWriteTimeout = DownloadTimeoutMilliseconds;

            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (MemoryStream memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        /// <summary>
        /// True when the downloaded bytes are a whole script. A truncated download or
        /// an error page is refused before it replaces anything.
        /// </summary>
        private static bool IsWholeScript(string fileName, byte[] content, out string error)
        {
            error = null;

            if (content == null || content.Length == 0)
            {
                error = "The script " + fileName + " was downloaded empty.";
                return false;
            }

            if (string.Equals(fileName, ScriptHash.MasFileName, StringComparison.OrdinalIgnoreCase))
            {
                if (content.Length < MinimumAllInOneSize)
                {
                    error = "The script " + fileName + " was downloaded incomplete.";
                    return false;
                }

                string text = Encoding.ASCII.GetString(content);
                if (text.IndexOf("@set masver=", StringComparison.Ordinal) < 0)
                {
                    error = "The file " + fileName + " is not the script of the project.";
                    return false;
                }

                foreach (string required in RequiredSwitches)
                {
                    if (text.IndexOf(required, StringComparison.Ordinal) < 0)
                    {
                        error = "The updated script does not know the switch " + required +
                                ", so this version is not used.";
                        return false;
                    }
                }

                return true;
            }

            if (content.Length < MinimumScriptSize)
            {
                error = "The script " + fileName + " was downloaded incomplete.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Writes a script through a temporary file, so that a failure does not leave a
        /// half written script behind.
        /// </summary>
        private static void WriteFile(string path, byte[] content)
        {
            string temporary = path + ".new";

            File.WriteAllBytes(temporary, content);

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }

        private static void WriteManifest(string folder, string commit, Dictionary<string, byte[]> files)
        {
            StringBuilder content = new StringBuilder();
            content.Append("Commit=").Append(commit).AppendLine();
            content.Append("Updated=").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).AppendLine();

            foreach (KeyValuePair<string, byte[]> file in files)
            {
                content.Append(file.Key).Append('=').Append(ScriptHash.OfBytes(file.Value)).AppendLine();
            }

            string path = Path.Combine(folder, ManifestFileName);
            string temporary = path + ".new";

            File.WriteAllText(temporary, content.ToString(), new UTF8Encoding(false));

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }

        private static Dictionary<string, string> ReadManifest()
        {
            Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string path = Path.Combine(AppPaths.GetMasFolder(), ManifestFileName);
                if (!File.Exists(path))
                {
                    return values;
                }

                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    values[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
                }
            }
            catch (Exception)
            {
                // Without a readable manifest the bundled scripts are used.
            }

            return values;
        }

        private static void DeleteManifest()
        {
            try
            {
                string path = Path.Combine(AppPaths.GetMasFolder(), ManifestFileName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("The manifest of the activation scripts could not be removed. " + ex.Message);
            }
        }

        private static bool IsInMasFolder(string path)
        {
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(folder))
                {
                    return false;
                }

                return string.Equals(
                    Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(AppPaths.GetMasFolder()).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsHash(string value)
        {
            return !string.IsNullOrEmpty(value) && Regex.IsMatch(value, "^[0-9A-Fa-f]{64}$");
        }

        private static bool IsCommit(string value)
        {
            return !string.IsNullOrEmpty(value) && Regex.IsMatch(value, "^[0-9A-Fa-f]{40}$");
        }

        private static string ShortCommit(string commit)
        {
            return string.IsNullOrEmpty(commit) || commit.Length <= 12 ? commit : commit.Substring(0, 12);
        }

        private static string ReadBundledScript()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("WinFreshUtil.Payloads." + ScriptHash.MasFileName))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Report(Action<string> onOutput, string text)
        {
            LogService.Info(text);

            if (onOutput != null)
            {
                onOutput(text);
            }
        }

        private static MasUpdateOutcome Failed(string message)
        {
            return new MasUpdateOutcome
            {
                Success = false,
                Message = message
            };
        }
    }
}
