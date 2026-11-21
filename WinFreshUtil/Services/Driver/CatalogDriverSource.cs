using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services.Driver
{
    /// <summary>
    /// The drivers that the Microsoft Update Catalog has for a device.
    ///
    /// The catalog is asked with the ids of the device. The id that the device is
    /// compatible with comes first, because that is the one the catalog knows. The
    /// file of the driver is a cabinet, which is unpacked and then given to Windows.
    /// </summary>
    public sealed class CatalogDriverSource : IDriverSource
    {
        private const string SearchUrl = "https://www.catalog.update.microsoft.com/Search.aspx?q=";
        private const string DownloadUrl = "https://www.catalog.update.microsoft.com/DownloadDialog.aspx";

        public string Name
        {
            get { return "Microsoft Update Catalog"; }
        }

        /// <summary>
        /// Looks for the drivers of a device. Runs on a background thread.
        /// </summary>
        public IList<DriverCandidate> Find(ProblemDevice device, Action<string> progress)
        {
            List<DriverCandidate> found = new List<DriverCandidate>();

            if (device == null)
            {
                return found;
            }

            foreach (string term in GetSearchTerms(device))
            {
                Report(progress, "Looking in the Microsoft Update Catalog for " + term + "...");

                string html;
                try
                {
                    html = DriverTools.DownloadString(SearchUrl + Uri.EscapeDataString(term));
                }
                catch (Exception ex)
                {
                    LogService.Error("The search in the update catalog failed.", ex);
                    Report(progress, "The update catalog could not be asked. " + ex.Message);
                    break;
                }

                if (string.IsNullOrEmpty(html))
                {
                    continue;
                }

                foreach (DriverCandidate candidate in ParseRows(html, term))
                {
                    if (!Contains(found, candidate.UpdateId))
                    {
                        found.Add(candidate);
                    }
                }

                if (found.Count > 0)
                {
                    break;
                }
            }

            if (found.Count == 0)
            {
                Report(progress, "The update catalog has no driver for this device.");
            }
            else
            {
                Report(progress, "The update catalog has " + found.Count + " driver(s) for this device.");
            }

            return found;
        }

        /// <summary>
        /// Downloads the driver, unpacks it and gives it to Windows. Runs on a
        /// background thread with administrator rights.
        /// </summary>
        public bool Install(DriverCandidate candidate, DriverInstallContext context)
        {
            if (candidate == null)
            {
                return false;
            }

            context.Write("The address of the driver is asked for at the update catalog...");

            string url;
            try
            {
                url = GetFileUrl(candidate.UpdateId);
            }
            catch (Exception ex)
            {
                LogService.Error("The address of the driver could not be read.", ex);
                context.Write("The address of the driver could not be read. " + ex.Message);
                return false;
            }

            if (string.IsNullOrEmpty(url))
            {
                context.Write("The update catalog has no file for this driver.");
                return false;
            }

            context.Write("The driver is downloaded from " + url);

            if (context.StopIfAsked("The installation was stopped."))
            {
                return false;
            }

            string folder = Path.Combine(Path.GetTempPath(), "WinFreshUtil", "drivers");

            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                context.Write("The folder for the driver could not be made. " + ex.Message);
                return false;
            }

            string cab = Path.Combine(folder, "driver-" + candidate.UpdateId + ".cab");

            try
            {
                DriverTools.DownloadFile(url, cab);
            }
            catch (Exception ex)
            {
                LogService.Error("The driver could not be downloaded.", ex);
                context.Write("The driver could not be downloaded. " + ex.Message);
                return false;
            }

            context.Write("The driver was downloaded (" + new FileInfo(cab).Length + " bytes).");

            string target = Path.Combine(folder, "driver-" + candidate.UpdateId);

            try
            {
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                }

                Directory.CreateDirectory(target);
            }
            catch (Exception ex)
            {
                context.Write("The folder for the driver could not be made. " + ex.Message);
                return false;
            }

            context.Write("The driver is unpacked...");

            if (context.StopIfAsked("The installation was stopped."))
            {
                return false;
            }

            // The folder may not end with a backslash, expand.exe needs a plain folder.
            int expandCode;
            string expandOutput = DriverTools.RunProgram(DriverTools.GetSystemProgram("expand.exe"),
                new[] { "-F:*", cab, target }, 300000, out expandCode);

            if (expandCode != 0)
            {
                context.Write("The driver could not be unpacked. " + expandOutput);
                return false;
            }

            context.Write("The driver was unpacked.");

            string[] infFiles = Directory.GetFiles(target, "*.inf", SearchOption.AllDirectories);
            if (infFiles.Length == 0)
            {
                context.Write("The driver has no driver information file (.inf). " + expandOutput);
                return false;
            }

            bool success = false;

            foreach (string inf in infFiles)
            {
                if (context.StopIfAsked("The installation was stopped."))
                {
                    return false;
                }

                context.Write("The driver \"" + Path.GetFileName(inf) + "\" is installed...");

                int pnputilCode;
                string output = DriverTools.RunProgram(DriverTools.GetSystemProgram("pnputil.exe"),
                    new[] { "/add-driver", inf, "/install" }, 300000, out pnputilCode);
                context.Write(output);

                if (pnputilCode == 0 || output.IndexOf("successfully", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    success = true;
                }
            }

            if (!success)
            {
                context.Write("Windows did not accept the driver \"" + candidate.Name + "\".");
                return false;
            }

            context.Write("The driver \"" + candidate.Name + "\" was installed.");
            return true;
        }

        /// <summary>
        /// Asks the update catalog for the address of the file of an update.
        /// </summary>
        public static string GetFileUrl(string updateId)
        {
            if (string.IsNullOrEmpty(updateId))
            {
                return null;
            }

            string post = "{\"size\":0,\"updateID\":\"" + updateId + "\",\"uidInfo\":\"" + updateId + "\"}";
            string body = "updateIDs=" + Uri.EscapeDataString("[" + post + "]");

            string html;

            using (WebClient client = new WebClient())
            {
                client.Headers.Add(HttpRequestHeader.UserAgent, Agent);
                client.Headers.Add(HttpRequestHeader.ContentType, "application/x-www-form-urlencoded");
                client.Headers.Add(HttpRequestHeader.Referer, SearchUrl);
                html = client.UploadString(DownloadUrl, "POST", body);
            }

            html = html.Replace("www.download.windowsupdate", "download.windowsupdate");

            Match match = Regex.Match(html, "downloadInformation\\[0\\]\\.files\\[\\d+\\]\\.url\\s*=\\s*'([^']*)'");

            if (!match.Success)
            {
                match = Regex.Match(html, "downloadInformation\\[0\\]\\.files\\[0\\]\\.url\\s*=\\s*'([^']*)'");
            }

            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// The texts that are used to look for the device in the update catalog.
        ///
        /// The catalog knows the ids that the device is compatible with, and it knows
        /// the hardware id without the part of the manufacturer and the version. The
        /// part of the device itself is never in the catalog, so it is only used when
        /// nothing else was found.
        /// </summary>
        public static IList<string> GetSearchTerms(ProblemDevice device)
        {
            List<string> terms = new List<string>();

            if (device == null)
            {
                return terms;
            }

            // The ids that the device is compatible with come first. The catalog has
            // them the way they are written here.
            if (device.CompatibleIds != null)
            {
                foreach (string id in device.CompatibleIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        Add(terms, id);
                    }
                }
            }

            // The hardware id of the device comes after it. The catalog does not know
            // the part of the manufacturer and the version, and it does not know the
            // part of the device itself either.
            if (device.HardwareIds != null)
            {
                foreach (string id in device.HardwareIds)
                {
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    string withoutInstance = DropInstance(id);
                    string baseId = withoutInstance ?? id;

                    Add(terms, DropSubsystem(baseId));

                    if (withoutInstance != null)
                    {
                        Add(terms, withoutInstance);
                    }

                    // The whole id is the last one to try.
                    Add(terms, id);
                }
            }

            return terms;
        }

        /// <summary>
        /// Takes the part of the device away, so that only the kind of the device is
        /// left. Returns null when there is no such part.
        /// </summary>
        private static string DropInstance(string id)
        {
            int instance = id.LastIndexOf('\\');

            if (instance <= 0)
            {
                return null;
            }

            string withoutInstance = id.Substring(0, instance);

            return LooksLikeHardwareId(withoutInstance) ? withoutInstance : null;
        }

        /// <summary>
        /// Takes the part of the manufacturer and the version away.
        /// </summary>
        private static string DropSubsystem(string id)
        {
            int subsystem = id.IndexOf("&SUBSYS_", StringComparison.OrdinalIgnoreCase);

            if (subsystem <= 0)
            {
                return id;
            }

            string shortId = id.Substring(0, subsystem);

            int revision = shortId.LastIndexOf('\\');

            if (revision > 0)
            {
                string withoutRevision = shortId.Substring(0, revision);

                if (LooksLikeHardwareId(withoutRevision))
                {
                    return withoutRevision;
                }
            }

            return shortId;
        }

        /// <summary>
        /// True when the text still holds the part of a hardware id that the catalog
        /// knows, so that short texts like "USB" are not looked for.
        /// </summary>
        private static bool LooksLikeHardwareId(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return text.IndexOf("VID_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("PCI\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void Add(List<string> list, string value)
        {
            if (string.IsNullOrEmpty(value) || list.Contains(value))
            {
                return;
            }

            list.Add(value);
        }

        /// <summary>
        /// Reads the drivers out of the answer of the update catalog.
        /// </summary>
        private static IList<DriverCandidate> ParseRows(string html, string term)
        {
            List<DriverCandidate> found = new List<DriverCandidate>();

            Match table = Regex.Match(html, "(?s)<table[^>]*id=\"ctl00_catalogBody_updateMatches\".*?</table>");

            if (!table.Success)
            {
                return found;
            }

            foreach (Match row in Regex.Matches(table.Value, "(?s)<tr[^>]*>.*?</tr>"))
            {
                if (row.Value.IndexOf("headerRow", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                Match id = Regex.Match(row.Value, "<tr id=\"([0-9a-fA-F-]{36})_R\\d+\"");

                if (!id.Success)
                {
                    continue;
                }

                string updateId = id.Groups[1].Value;
                string title = ReadElementText(row.Value, updateId + "_link");
                string date = ReadColumnText(row.Value, 4);
                string size = ReadElementText(row.Value, updateId + "_size");

                found.Add(new DriverCandidate
                {
                    Name = string.IsNullOrEmpty(title) ? "Driver " + updateId : title,
                    Version = DriverLookup.ExtractVersion(title),
                    Released = date,
                    Source = "Microsoft Update Catalog",
                    SourceUrl = SearchUrl + Uri.EscapeDataString(term),
                    HardwareId = term,
                    UpdateId = updateId,
                    Stage = DriverStage.UpdateCatalog,
                    Size = ReadSizeText(size)
                });
            }

            return found;
        }

        /// <summary>
        /// Reads the text of an element of a row of the update catalog. The element is
        /// found by its id, which is built from the id of the update.
        /// </summary>
        private static string ReadElementText(string row, string elementId)
        {
            Match match = Regex.Match(
                row,
                "id=[\"']" + Regex.Escape(elementId) + "[\"'][^>]*>(.*?)</",
                RegexOptions.Singleline);

            if (!match.Success)
            {
                return string.Empty;
            }

            return CleanText(match.Groups[1].Value);
        }

        /// <summary>
        /// Reads the text of the cell with the given number of a row.
        /// </summary>
        private static string ReadColumnText(string row, int column)
        {
            List<string> cells = new List<string>();

            foreach (Match cell in Regex.Matches(row, "(?s)<td[^>]*>(.*?)</td>"))
            {
                cells.Add(CleanText(cell.Groups[1].Value));
            }

            if (column < cells.Count)
            {
                return cells[column];
            }

            return string.Empty;
        }

        private static string CleanText(string html)
        {
            string text = Regex.Replace(html, "(?s)<[^>]+>", " ");
            text = WebUtility.HtmlDecode(text);

            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static long ReadSizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            Match match = Regex.Match(text, @"([0-9]+(?:\.[0-9]+)?)\s*(KB|MB|GB)", RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                return 0;
            }

            double number;
            if (!double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out number))
            {
                return 0;
            }

            string unit = match.Groups[2].Value.ToUpperInvariant();

            if (unit == "KB") number *= 1024;
            else if (unit == "MB") number *= 1024 * 1024;
            else if (unit == "GB") number *= 1024 * 1024 * 1024;

            return (long)number;
        }

        private static bool Contains(IList<DriverCandidate> list, string updateId)
        {
            if (string.IsNullOrEmpty(updateId))
            {
                return false;
            }

            foreach (DriverCandidate candidate in list)
            {
                if (string.Equals(candidate.UpdateId, updateId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Report(Action<string> progress, string text)
        {
            if (progress != null && !string.IsNullOrEmpty(text))
            {
                progress(text);
            }
        }


        private const string Agent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
    }
}
