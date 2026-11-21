using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Read-only inspection of the Windows and Microsoft Office license state.
    ///
    /// Nothing in this class changes licensing: it only reads what is already
    /// reported by the software protection platform and by the Office licensing tool.
    /// </summary>
    public static class LicenseService
    {
        private const string WindowsApplicationId = "55c92734-d682-4d71-983e-d6ec3f16059f";
        private const string OfficeApplicationId = "0ff1ce15-a989-479d-af46-f275c6370663";

        private const string WmiNamespace = @"root\CIMV2";

        /// <summary>
        /// Collects the license state of the machine. Safe to call from a background thread.
        /// </summary>
        public static LicenseSnapshot Scan()
        {
            LicenseSnapshot snapshot = new LicenseSnapshot();

            try
            {
                ReadFromWmi(snapshot);
            }
            catch (Exception ex)
            {
                snapshot.Error = "License information could not be read: " + ex.Message;
                LogService.Error("The license information could not be read.", ex);
            }

            try
            {
                ReadFirmwareKey(snapshot);
            }
            catch (Exception ex)
            {
                snapshot.Notes.Add("The firmware product key could not be read: " + ex.Message);
                LogService.Error("The firmware product key could not be read.", ex);
            }

            // Office 2013 and newer are exposed through WMI. Older versions are not,
            // so fall back to the Office licensing tool when WMI reported nothing.
            if (snapshot.OfficeProducts.Count == 0)
            {
                try
                {
                    ReadOfficeFromOspp(snapshot);
                }
                catch (Exception ex)
                {
                    snapshot.Notes.Add("The Microsoft Office license status could not be read: " + ex.Message);
                    LogService.Error("The Microsoft Office license status could not be read.", ex);
                }
            }

            return snapshot;
        }

        private static void ReadFromWmi(LicenseSnapshot snapshot)
        {
            const string query =
                "SELECT Name, Description, PartialProductKey, LicenseStatus, GracePeriodRemaining, ApplicationID " +
                "FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL";

            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(WmiNamespace, query))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementObject obj in results)
                {
                    string applicationId = GetString(obj, "ApplicationID");

                    LicenseEntry entry = new LicenseEntry
                    {
                        ProductName = GetString(obj, "Name"),
                        Description = GetString(obj, "Description"),
                        PartialProductKey = GetString(obj, "PartialProductKey"),
                        State = ToLicenseState(GetUInt32(obj, "LicenseStatus")),
                        GraceDays = GetGraceDays(obj)
                    };

                    if (string.Equals(applicationId, WindowsApplicationId, StringComparison.OrdinalIgnoreCase))
                    {
                        snapshot.WindowsProducts.Add(entry);
                    }
                    else if (string.Equals(applicationId, OfficeApplicationId, StringComparison.OrdinalIgnoreCase))
                    {
                        snapshot.OfficeProducts.Add(entry);
                    }
                }
            }

            if (snapshot.OfficeProducts.Count > 0)
            {
                snapshot.OfficeDetectionSource = "Windows Management Instrumentation";
            }
        }

        private static void ReadFirmwareKey(LicenseSnapshot snapshot)
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                       WmiNamespace, "SELECT OA3xOriginalProductKey FROM SoftwareLicensingService"))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementObject obj in results)
                {
                    string key = GetString(obj, "OA3xOriginalProductKey");
                    snapshot.FirmwareKeyPresent = !string.IsNullOrWhiteSpace(key) && !IsAllZeroKey(key);
                    break;
                }
            }
        }

        private static void ReadOfficeFromOspp(LicenseSnapshot snapshot)
        {
            string ospp = FindOspp();
            if (ospp == null)
            {
                snapshot.Notes.Add("No Microsoft Office installation was detected on this machine.");
                return;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo("cscript.exe")
            {
                Arguments = "//Nologo \"" + ospp + "\" /dstatus",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // The licensing tool writes to the console, so its output uses the OEM code page.
            try
            {
                startInfo.StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                startInfo.StandardErrorEncoding = startInfo.StandardOutputEncoding;
            }
            catch (Exception)
            {
                // Fall back to the default encoding.
            }

            string output;
            string error;

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.Start();

                // Read both streams concurrently so a full pipe buffer cannot deadlock the tool.
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(30000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception)
                    {
                        // The process already exited on its own.
                    }

                    // Observe both tasks so a broken pipe is not reported as an
                    // unobserved task exception later on.
                    Observe(outputTask);
                    Observe(errorTask);

                    snapshot.Notes.Add("The Microsoft Office licensing tool did not finish in time.");
                    return;
                }

                process.WaitForExit();
                output = outputTask.GetAwaiter().GetResult();
                error = errorTask.GetAwaiter().GetResult();
            }

            IList<LicenseEntry> entries = ParseOsppOutput(output);
            foreach (LicenseEntry entry in entries)
            {
                snapshot.OfficeProducts.Add(entry);
            }

            snapshot.OfficeDetectionSource = "Microsoft Office licensing tool";

            if (entries.Count == 0 && !string.IsNullOrWhiteSpace(error))
            {
                snapshot.Notes.Add("The Microsoft Office licensing tool reported: " + error.Trim());
            }
        }

        /// <summary>
        /// Parses the output of "ospp.vbs /dstatus". The labels of that tool are always
        /// emitted in English, so the parsing does not depend on the Windows display language.
        /// </summary>
        private static IList<LicenseEntry> ParseOsppOutput(string output)
        {
            List<LicenseEntry> entries = new List<LicenseEntry>();
            if (string.IsNullOrEmpty(output))
            {
                return entries;
            }

            LicenseEntry current = null;
            string[] lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("SKU ID:", StringComparison.OrdinalIgnoreCase))
                {
                    current = new LicenseEntry
                    {
                        ProductName = "Unknown product",
                        Description = string.Empty,

                        // Start from Unknown so that an unrecognised status line can never
                        // be mistaken for "unlicensed".
                        State = LicenseState.Unknown
                    };
                    entries.Add(current);
                    continue;
                }

                if (current == null)
                {
                    continue;
                }

                string value;
                if (TryTake(line, "LICENSE NAME:", out value))
                {
                    current.ProductName = value;
                }
                else if (TryTake(line, "LICENSE DESCRIPTION:", out value))
                {
                    current.Description = value;
                }
                else if (TryTake(line, "LICENSE STATUS:", out value))
                {
                    current.State = ParseOsppStatusToken(value, current.State);
                }
                else if (TryTake(line, "ERROR CODE:", out value))
                {
                    current.State = ParseOsppErrorCode(value, current.State);
                }
                else if (TryTake(line, "Last 5 characters of installed product key:", out value))
                {
                    current.PartialProductKey = value;
                }
                else if (TryTake(line, "REMAINING GRACE:", out value))
                {
                    current.GraceDays = ParseGraceDays(value);
                }
            }

            return entries;
        }

        /// <summary>
        /// Maps the ospp "ERROR CODE" value. These numbers are the client software overview
        /// error codes and are not the same as the WMI LicenseStatus values.
        /// </summary>
        private static LicenseState ParseOsppErrorCode(string value, LicenseState fallback)
        {
            string number = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
            int code;
            if (!int.TryParse(number, out code))
            {
                return fallback;
            }

            switch (code)
            {
                case 0:
                    return LicenseState.Licensed;
                case 1:
                    return LicenseState.Unlicensed;
                case 2:
                    return LicenseState.Notification;
                case 3:
                    return LicenseState.OutOfBoxGrace;
                case 4:
                    return LicenseState.OutOfToleranceGrace;
                case 5:
                    return LicenseState.NonGenuineGrace;
                case 6:
                    return LicenseState.ExtendedGrace;
                default:
                    return LicenseState.Unknown;
            }
        }

        /// <summary>
        /// Maps the ospp "LICENSE STATUS" token such as ---LICENSED---.
        /// </summary>
        private static LicenseState ParseOsppStatusToken(string value, LicenseState fallback)
        {
            int first = value.IndexOf("---", StringComparison.Ordinal);
            int last = value.LastIndexOf("---", StringComparison.Ordinal);
            if (first < 0 || last <= first)
            {
                return fallback;
            }

            string token = value.Substring(first + 3, last - first - 3).Trim().ToUpperInvariant();
            token = Regex.Replace(token, "[^A-Z]", string.Empty);

            switch (token)
            {
                case "LICENSED":
                    return LicenseState.Licensed;
                case "UNLICENSED":
                    return LicenseState.Unlicensed;
                case "NOTIFICATION":
                    return LicenseState.Notification;
                case "OOBGRACE":
                    return LicenseState.OutOfBoxGrace;
                case "OOTGRACE":
                    return LicenseState.OutOfToleranceGrace;
                case "NONGENUINEGRACE":
                    return LicenseState.NonGenuineGrace;
                case "EXTENDEDGRACE":
                    return LicenseState.ExtendedGrace;
                default:
                    return fallback;
            }
        }

        private static int? ParseGraceDays(string value)
        {
            Match match = Regex.Match(value, @"(\d+)\s*day", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            int days;
            if (!int.TryParse(match.Groups[1].Value, out days))
            {
                return null;
            }

            return days;
        }

        private static void Observe(Task task)
        {
            try
            {
                task.Wait(2000);
            }
            catch (Exception)
            {
                // The task failed, for example because the pipe was closed. Nothing to do.
            }
        }

        private static bool TryTake(string line, string prefix, out string value)
        {
            value = null;
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            value = line.Substring(prefix.Length).Trim();
            return true;
        }

        private static string FindOspp()
        {
            foreach (string programFiles in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                     })
            {
                if (string.IsNullOrEmpty(programFiles))
                {
                    continue;
                }

                string officeRoot = Path.Combine(programFiles, "Microsoft Office");
                if (!Directory.Exists(officeRoot))
                {
                    continue;
                }

                string found = FindFileInTree(officeRoot, "OSPP.VBS");
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string FindFileInTree(string directory, string fileName)
        {
            try
            {
                string[] files = Directory.GetFiles(directory, fileName);
                if (files.Length > 0)
                {
                    return files[0];
                }
            }
            catch (Exception)
            {
                // Unreadable directory, continue with the remaining ones.
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception)
            {
                return null;
            }

            foreach (string child in directories)
            {
                string found = FindFileInTree(child, fileName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static LicenseState ToLicenseState(uint value)
        {
            switch (value)
            {
                case 0:
                    return LicenseState.Unlicensed;
                case 1:
                    return LicenseState.Licensed;
                case 2:
                    return LicenseState.OutOfBoxGrace;
                case 3:
                    return LicenseState.OutOfToleranceGrace;
                case 4:
                    return LicenseState.NonGenuineGrace;
                case 5:
                    return LicenseState.Notification;
                case 6:
                    return LicenseState.ExtendedGrace;
                default:
                    return LicenseState.Unknown;
            }
        }

        private static string GetString(ManagementObject obj, string property)
        {
            try
            {
                object value = obj[property];
                return value == null ? string.Empty : Convert.ToString(value);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static uint GetUInt32(ManagementObject obj, string property)
        {
            try
            {
                object value = obj[property];
                return value == null ? uint.MaxValue : Convert.ToUInt32(value);
            }
            catch (Exception)
            {
                return uint.MaxValue;
            }
        }

        private static int? GetGraceDays(ManagementObject obj)
        {
            try
            {
                object value = obj["GracePeriodRemaining"];
                if (value == null)
                {
                    return null;
                }

                uint minutes = Convert.ToUInt32(value);
                return (int)(minutes / 1440);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsAllZeroKey(string key)
        {
            foreach (char c in key)
            {
                if (c != '0' && c != '-')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
