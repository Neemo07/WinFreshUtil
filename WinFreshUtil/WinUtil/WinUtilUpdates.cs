using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.ServiceProcess;

namespace WinFreshUtil.WinUtil
{
    /// <summary>
    /// The three profiles of the Updates tab. Their names and their texts are the ones of
    /// the original project; the work is done with the registry, the service manager and
    /// the task scheduler of Windows, so nothing is downloaded.
    /// </summary>
    public static class WinUtilUpdates
    {
        private const string WindowsUpdatePolicy = @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        private const string AutomaticUpdatePolicy = WindowsUpdatePolicy + @"\AU";
        private const string DeviceMetadataPolicy = @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\Device Metadata";
        private const string DriverSearchingPolicy = @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\DriverSearching";
        private const string DeliveryOptimizationConfig =
            @"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config";
        private const string UpdateSettings = @"HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        private const string ExplorerPolicy =
            @"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        private const string ServiceKey = @"HKLM:\SYSTEM\CurrentControlSet\Services\";

        /// <summary>
        /// The folders of the task scheduler that belong to Windows Update.
        /// </summary>
        private static readonly string[] UpdateTaskFolders =
        {
            @"\Microsoft\Windows\InstallService\",
            @"\Microsoft\Windows\UpdateOrchestrator\",
            @"\Microsoft\Windows\UpdateAssistant\",
            @"\Microsoft\Windows\WaaSMedic\",
            @"\Microsoft\Windows\WindowsUpdate\",
            @"\Microsoft\WindowsUpdate\"
        };

        /// <summary>
        /// Balanced security and stability: the feature updates are deferred for a year,
        /// the quality updates for four days, the drivers are left out and the download is
        /// announced before it is installed.
        /// </summary>
        public static bool ApplyRecommended(Action<WinUtilLogLine> log, out string error)
        {
            error = null;
            bool ok = true;

            Report(log, "The recommended Windows Update settings are applied.", WinUtilLogLevel.Info);

            // A profile replaces the settings of the other profiles first.
            ok &= RemoveValue(AutomaticUpdatePolicy, "NoAutoUpdate", log);
            ok &= RemoveValue(DeliveryOptimizationConfig, "DODownloadMode", log);
            ok &= RemoveValue(WindowsUpdatePolicy, "DisableWindowsUpdateAccess", log);
            RemoveLocalhostServer(log);

            ok &= SetService("BITS", "Manual", false, log);
            ok &= SetService("wuauserv", "Manual", false, log);
            ok &= SetService("UsoSvc", "Automatic", true, log);
            ok &= EnableUpdateTasks(true, log);

            ok &= SetValue(DeviceMetadataPolicy, "PreventDeviceMetadataFromNetwork", 1, log);
            ok &= SetValue(DriverSearchingPolicy, "DontPromptForWindowsUpdate", 1, log);
            ok &= SetValue(DriverSearchingPolicy, "DontSearchWindowsUpdate", 1, log);
            ok &= SetValue(DriverSearchingPolicy, "DriverUpdateWizardWuSearchEnabled", 0, log);
            ok &= SetValue(WindowsUpdatePolicy, "ExcludeWUDriversInQualityUpdate", 1, log);
            ok &= SetValue(WindowsUpdatePolicy, "DeferFeatureUpdates", 1, log);
            ok &= SetValue(WindowsUpdatePolicy, "DeferFeatureUpdatesPeriodInDays", 365, log);
            ok &= SetValue(WindowsUpdatePolicy, "DeferQualityUpdates", 1, log);
            ok &= SetValue(WindowsUpdatePolicy, "DeferQualityUpdatesPeriodInDays", 4, log);

            // The older settings of the same name would override the policies above.
            RemoveValue(UpdateSettings, "BranchReadinessLevel", log);
            RemoveValue(UpdateSettings, "DeferFeatureUpdatesPeriodInDays", log);
            RemoveValue(UpdateSettings, "DeferQualityUpdatesPeriodInDays", log);

            ok &= RemoveValue(AutomaticUpdatePolicy, "NoAutoRebootWithLoggedOnUsers", log);
            ok &= SetValue(AutomaticUpdatePolicy, "AUOptions", 3, log);
            ok &= SetValue(AutomaticUpdatePolicy, "AUPowerManagement", 0, log);

            if (!ok)
            {
                error = "Some of the recommended settings could not be applied. The log names them.";
            }

            Report(log, ok
                ? "The recommended Windows Update settings are in place. Restart Windows to see them."
                : "The recommended Windows Update settings were only partly applied.",
                ok ? WinUtilLogLevel.Success : WinUtilLogLevel.Warning);

            return ok;
        }

        /// <summary>
        /// Takes every policy of the three profiles back and returns the update services
        /// and the update tasks to the state of Windows.
        /// </summary>
        public static bool RestoreDefaults(Action<WinUtilLogLine> log, out string error)
        {
            error = null;
            bool ok = true;

            Report(log, "The Windows Update settings are put back the way Windows has them.", WinUtilLogLevel.Info);

            foreach (string name in new[] { "NoAutoUpdate", "AUOptions", "NoAutoRebootWithLoggedOnUsers", "AUPowerManagement" })
            {
                RemoveValue(AutomaticUpdatePolicy, name, log);
            }

            foreach (string name in new[]
                     {
                         "ExcludeWUDriversInQualityUpdate", "DeferFeatureUpdates", "DeferFeatureUpdatesPeriodInDays",
                         "DeferQualityUpdates", "DeferQualityUpdatesPeriodInDays"
                     })
            {
                RemoveValue(WindowsUpdatePolicy, name, log);
            }

            foreach (string name in new[] { "BranchReadinessLevel", "DeferFeatureUpdatesPeriodInDays", "DeferQualityUpdatesPeriodInDays" })
            {
                RemoveValue(UpdateSettings, name, log);
            }

            RemoveValue(DeviceMetadataPolicy, "PreventDeviceMetadataFromNetwork", log);
            RemoveValue(DriverSearchingPolicy, "DontPromptForWindowsUpdate", log);
            RemoveValue(DriverSearchingPolicy, "DontSearchWindowsUpdate", log);
            RemoveValue(DriverSearchingPolicy, "DriverUpdateWizardWuSearchEnabled", log);
            RemoveValue(DeliveryOptimizationConfig, "DODownloadMode", log);

            // Only the value that this project writes is removed, so a page that the user
            // hid on purpose stays hidden.
            if (string.Equals(ReadValue(ExplorerPolicy, "SettingsPageVisibility"), "hide:windowsupdate",
                    StringComparison.OrdinalIgnoreCase))
            {
                RemoveValue(ExplorerPolicy, "SettingsPageVisibility", log);
            }

            ok &= SetService("BITS", "Manual", false, log);
            ok &= SetService("wuauserv", "Manual", false, log);
            ok &= SetService("UsoSvc", "Automatic", true, log);

            // The update medic service is protected, so its start value is written directly.
            if (!string.Equals(WinUtilServices.ReadStartupType("WaaSMedicSvc"), "Manual", StringComparison.OrdinalIgnoreCase))
            {
                SetValue(ServiceKey + "WaaSMedicSvc", "Start", 3, log);
            }

            ok &= EnableUpdateTasks(true, log);

            if (!ok)
            {
                error = "Some of the settings could not be put back. The log names them.";
            }

            Report(log, ok
                ? "The Windows Update settings are back to the defaults of Windows."
                : "The Windows Update settings were only partly put back.",
                ok ? WinUtilLogLevel.Success : WinUtilLogLevel.Warning);

            return ok;
        }

        /// <summary>
        /// Stops the update services, disables the update tasks and removes the files that
        /// were downloaded for an update. Windows cannot install updates until the
        /// defaults are restored.
        /// </summary>
        public static bool Disable(Action<WinUtilLogLine> log, out string error)
        {
            error = null;
            bool ok = true;

            Report(log, "Windows Update is disabled. Restore the defaults to turn it on again.", WinUtilLogLevel.Warning);

            ok &= SetValue(AutomaticUpdatePolicy, "NoAutoUpdate", 1, log);
            ok &= SetValue(AutomaticUpdatePolicy, "AUOptions", 1, log);
            ok &= SetValue(DeliveryOptimizationConfig, "DODownloadMode", 0, log);

            ok &= StopService("BITS", log);
            ok &= StopService("wuauserv", log);
            ok &= StopService("UsoSvc", log);

            ok &= SetService("BITS", "Disabled", false, log);
            ok &= SetService("wuauserv", "Disabled", false, log);
            ok &= SetService("UsoSvc", "Disabled", false, log);

            ClearUpdateCache(log);

            ok &= EnableUpdateTasks(false, log);

            if (!ok)
            {
                error = "Some of the services could not be disabled. The log names them.";
            }

            Report(log, ok
                ? "Windows Update is disabled. Restart Windows for every change to take effect."
                : "Windows Update was only partly disabled.",
                ok ? WinUtilLogLevel.Success : WinUtilLogLevel.Warning);

            return ok;
        }

        /// <summary>
        /// Enables or disables every task of the Windows Update folders.
        /// </summary>
        private static bool EnableUpdateTasks(bool enable, Action<WinUtilLogLine> log)
        {
            List<string> tasks = new List<string>();
            string processError;

            WinUtilProcess.Run(
                "schtasks.exe",
                "/Query /FO CSV /NH",
                delegate(string line)
                {
                    string name = FirstCsvField(line);
                    if (!string.IsNullOrEmpty(name) && IsUpdateTask(name))
                    {
                        tasks.Add(name);
                    }
                },
                out processError,
                120 * 1000);

            if (tasks.Count == 0)
            {
                Report(log, "No Windows Update task was found.", WinUtilLogLevel.Info);
                return true;
            }

            int changed = 0;
            int failed = 0;

            foreach (string task in tasks)
            {
                string taskError;
                int code = WinUtilProcess.Run(
                    "schtasks.exe",
                    "/Change /TN " + WinUtilProcess.Quote(task) + (enable ? " /ENABLE" : " /DISABLE"),
                    null,
                    out taskError,
                    60 * 1000);

                if (code == 0)
                {
                    changed++;
                }
                else
                {
                    failed++;
                }
            }

            Report(log, changed + " of " + tasks.Count + " Windows Update task(s) were " +
                       (enable ? "enabled." : "disabled."),
                failed == 0 ? WinUtilLogLevel.Info : WinUtilLogLevel.Warning);

            return failed == 0;
        }

        private static bool IsUpdateTask(string name)
        {
            foreach (string folder in UpdateTaskFolders)
            {
                if (name.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The first field of a line of the CSV output of schtasks.
        /// </summary>
        private static string FirstCsvField(string line)
        {
            string text = (line ?? string.Empty).Trim();
            if (text.Length < 2 || text[0] != '"')
            {
                return null;
            }

            System.Text.StringBuilder result = new System.Text.StringBuilder();

            for (int i = 1; i < text.Length; i++)
            {
                char character = text[i];

                if (character == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        result.Append('"');
                        i++;
                        continue;
                    }

                    return result.ToString();
                }

                result.Append(character);
            }

            return null;
        }

        /// <summary>
        /// Removes the files that Windows downloaded for an update. Files that are in use
        /// are left alone, because the update service may still hold them.
        /// </summary>
        private static void ClearUpdateCache(Action<WinUtilLogLine> log)
        {
            string root = Environment.GetEnvironmentVariable("SystemRoot");
            if (string.IsNullOrEmpty(root))
            {
                root = @"C:\Windows";
            }

            string folder = Path.Combine(root, "SoftwareDistribution");
            if (!Directory.Exists(folder))
            {
                return;
            }

            int removed = 0;
            int left = 0;

            try
            {
                foreach (FileSystemInfo item in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                {
                    try
                    {
                        DirectoryInfo directory = item as DirectoryInfo;
                        if (directory != null)
                        {
                            directory.Delete(true);
                        }
                        else
                        {
                            item.Delete();
                        }

                        removed++;
                    }
                    catch (Exception)
                    {
                        // A file that is in use is left for Windows to remove.
                        left++;
                    }
                }
            }
            catch (Exception ex)
            {
                Report(log, "The downloaded update files could not be listed. " + ex.Message, WinUtilLogLevel.Warning);
                return;
            }

            Report(log, "The downloaded update files were cleared (" + removed + " removed, " + left + " in use).",
                WinUtilLogLevel.Info);
        }

        private static void RemoveLocalhostServer(Action<WinUtilLogLine> log)
        {
            if (!string.Equals(ReadValue(WindowsUpdatePolicy, "WUServer"), "http://localhost:8080",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            RemoveValue(WindowsUpdatePolicy, "WUServer", log);
            RemoveValue(WindowsUpdatePolicy, "WUStatusServer", log);
            RemoveValue(AutomaticUpdatePolicy, "UseWUServer", log);
        }

        private static string ReadValue(string path, string name)
        {
            object value;
            WinUtilRegistry.TryRead(
                new WinUtilRegistryChange { Path = path, Name = name },
                out value);

            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool SetValue(string path, string name, int value, Action<WinUtilLogLine> log)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);

            try
            {
                WinUtilRegistry.Apply(
                    new WinUtilRegistryChange { Path = path, Name = name, Type = "DWord", Value = text },
                    text);

                Report(log, "Registry: " + path + "\\" + name + " = " + text, WinUtilLogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                Report(log, "The registry value " + path + "\\" + name + " could not be set. " + ex.Message,
                    WinUtilLogLevel.Error);
                return false;
            }
        }

        private static bool RemoveValue(string path, string name, Action<WinUtilLogLine> log)
        {
            try
            {
                WinUtilRegistry.Apply(
                    new WinUtilRegistryChange { Path = path, Name = name },
                    WinUtilRegistry.RemoveEntry);

                return true;
            }
            catch (Exception ex)
            {
                Report(log, "The registry value " + path + "\\" + name + " could not be removed. " + ex.Message,
                    WinUtilLogLevel.Error);
                return false;
            }
        }

        private static bool SetService(string name, string startupType, bool start, Action<WinUtilLogLine> log)
        {
            string error;
            if (!WinUtilServices.SetStartupType(name, startupType, out error))
            {
                Report(log, error, WinUtilLogLevel.Warning);
                return false;
            }

            Report(log, "Service " + name + " is set to " + startupType + ".", WinUtilLogLevel.Info);

            if (!start)
            {
                return true;
            }

            try
            {
                using (ServiceController service = new ServiceController(name))
                {
                    if (service.Status != ServiceControllerStatus.Running)
                    {
                        service.Start();
                        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Report(log, "The service " + name + " could not be started. " + ex.Message, WinUtilLogLevel.Warning);
                return false;
            }
        }

        private static bool StopService(string name, Action<WinUtilLogLine> log)
        {
            try
            {
                using (ServiceController service = new ServiceController(name))
                {
                    if (service.Status == ServiceControllerStatus.Stopped)
                    {
                        return true;
                    }

                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                }

                return true;
            }
            catch (Exception ex)
            {
                // A service that cannot be stopped is reported, but the startup type is
                // still set to Disabled afterwards.
                Report(log, "The service " + name + " could not be stopped. " + ex.Message, WinUtilLogLevel.Warning);
                return false;
            }
        }

        private static void Report(Action<WinUtilLogLine> log, string text, WinUtilLogLevel level)
        {
            if (log != null && !string.IsNullOrEmpty(text))
            {
                log(new WinUtilLogLine(text, level));
            }
        }
    }
}
