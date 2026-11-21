using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;

namespace WinFreshUtil.WinUtil
{
    /// <summary>
    /// The few buttons of the original features page whose work is a Windows tool rather
    /// than a PowerShell fragment in the definitions.
    /// </summary>
    public static class WinUtilActions
    {
        /// <summary>
        /// The template of the Ultimate Performance power plan of Windows.
        /// </summary>
        private const string UltimatePerformanceTemplate = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        private static readonly Regex SchemePattern = new Regex(
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.CultureInvariant);

        private static readonly HashSet<string> ToolButtons = new HashSet<string>(StringComparer.Ordinal)
        {
            "WPFFixesNTPPool",
            "WPFFixesNetwork",
            "WPFPanelDISM",
            "WPFWinUtilSSHServer",
            "WPFAddUltPerf",
            "WPFRemoveUltPerf",
            "WPFUpdatessecurity",
            "WPFUpdatesdefault",
            "WPFUpdatesdisable"
        };

        /// <summary>
        /// The buttons that only open a window of Windows. They are started as the user who
        /// is looking at the page, so no administrator prompt is shown for them.
        /// </summary>
        private static readonly Dictionary<string, string> Panels =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "WPFPanelComputer", "compmgmt.msc" },
                { "WPFPanelControl", "control.exe" },
                { "WPFPanelMouse", "main.cpl" },
                { "WPFPanelNetwork", "ncpa.cpl" },
                { "WPFPanelPower", "powercfg.cpl" },
                { "WPFPanelPrinter", "control.exe" },
                { "WPFPanelPrograms", "appwiz.cpl" },
                { "WPFPanelRegion", "intl.cpl" },
                { "WPFPanelSecurity", "wscui.cpl" },
                { "WPFPanelSound", "mmsys.cpl" },
                { "WPFPanelSystem", "sysdm.cpl" },
                { "WPFPanelTimedate", "timedate.cpl" },
                { "WPFPanelFirewall", "firewall.cpl" },
                { "WPFPanelRestore", "rstrui.exe" }
            };

        /// <summary>
        /// True when the entry only opens a window of Windows.
        /// </summary>
        public static bool IsPanel(WinUtilEntry entry)
        {
            return entry != null && Panels.ContainsKey(entry.Key);
        }

        /// <summary>
        /// True when this build knows how to run the button, so a new button in the
        /// definitions cannot end up doing nothing when it is pressed.
        /// </summary>
        public static bool CanRun(WinUtilEntry entry)
        {
            return entry != null && (Panels.ContainsKey(entry.Key) || ToolButtons.Contains(entry.Key));
        }

        /// <summary>
        /// Opens the window of Windows that the button names. Returns false when the entry
        /// is not one of those buttons, so the caller can run the real work instead.
        /// </summary>
        public static bool TryOpenPanel(WinUtilEntry entry, out bool opened, out string error)
        {
            opened = false;
            error = null;

            if (entry == null)
            {
                return false;
            }

            string file;
            if (!Panels.TryGetValue(entry.Key, out file))
            {
                return false;
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(file)
                {
                    UseShellExecute = true
                };

                if (string.Equals(entry.Key, "WPFPanelPrinter", StringComparison.Ordinal))
                {
                    startInfo.Arguments = "printers";
                }

                Process.Start(startInfo);
                opened = true;
                return true;
            }
            catch (Exception ex)
            {
                error = "The window " + entry.ButtonText + " could not be opened. " + ex.Message;
                return true;
            }
        }

        public static bool Run(WinUtilEntry entry, Action<WinUtilLogLine> log, out string error)
        {
            error = null;

            if (entry == null)
            {
                error = "The entry is empty.";
                return false;
            }

            bool opened;
            string openError;
            if (TryOpenPanel(entry, out opened, out openError))
            {
                if (!opened)
                {
                    error = openError;
                    Report(log, openError, WinUtilLogLevel.Error);
                }

                return opened;
            }

            switch (entry.Key)
            {
                case "WPFFixesNTPPool":
                    return RunNtp(log, out error);
                case "WPFFixesNetwork":
                    return ResetNetwork(log, out error);
                case "WPFPanelDISM":
                    return RepairSystem(log, out error);
                case "WPFWinUtilSSHServer":
                    return EnableSshServer(log, out error);
                case "WPFAddUltPerf":
                    return AddUltimatePerformance(log, out error);
                case "WPFRemoveUltPerf":
                    return RemoveUltimatePerformance(log, out error);
                case "WPFUpdatessecurity":
                    return WinUtilUpdates.ApplyRecommended(log, out error);
                case "WPFUpdatesdefault":
                    return WinUtilUpdates.RestoreDefaults(log, out error);
                case "WPFUpdatesdisable":
                    return WinUtilUpdates.Disable(log, out error);
                default:
                    error = "This entry is not available in the offline build.";
                    Report(log, entry.ButtonText + " is not available in the offline build.", WinUtilLogLevel.Warning);
                    return false;
            }
        }

        private static bool AddUltimatePerformance(Action<WinUtilLogLine> log, out string error)
        {
            error = null;

            Report(log, "The Ultimate Performance power plan is copied and made active.", WinUtilLogLevel.Info);

            string scheme = null;
            int code = WinUtilProcess.Run(
                "powercfg.exe",
                "/duplicatescheme " + UltimatePerformanceTemplate,
                delegate(string line)
                {
                    Report(log, line, WinUtilLogLevel.Info);

                    if (scheme == null)
                    {
                        scheme = FindSchemeId(line);
                    }
                },
                out error,
                60 * 1000);

            if (code != 0)
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = "powercfg could not copy the Ultimate Performance plan (exit code " + code + ").";
                }

                Report(log, error, WinUtilLogLevel.Error);
                return false;
            }

            if (string.IsNullOrEmpty(scheme))
            {
                error = "powercfg did not report the plan that was made.";
                Report(log, error, WinUtilLogLevel.Error);
                return false;
            }

            if (!RunTool("powercfg.exe", "/setactive " + scheme, log, out error))
            {
                return false;
            }

            Report(log, "The Ultimate Performance power plan is active.", WinUtilLogLevel.Success);
            return true;
        }

        private static bool RemoveUltimatePerformance(Action<WinUtilLogLine> log, out string error)
        {
            error = null;

            Report(log, "The default power plans are put back.", WinUtilLogLevel.Info);

            if (!RunTool("powercfg.exe", "/restoredefaultschemes", log, out error))
            {
                return false;
            }

            Report(log, "The power plans are back to the defaults of Windows.", WinUtilLogLevel.Success);
            return true;
        }

        /// <summary>
        /// The identifier of a power plan in one line of the output of powercfg, or null.
        /// </summary>
        private static string FindSchemeId(string line)
        {
            Match match = SchemePattern.Match(line ?? string.Empty);
            return match.Success ? match.Value : null;
        }

        private static bool RunNtp(Action<WinUtilLogLine> log, out string error)
        {
            error = null;

            if (!StartService("w32time", log, out error))
            {
                return false;
            }

            if (!RunTool("w32tm.exe", "/config /update /manualpeerlist:\"pool.ntp.org,0x8\" /syncfromflags:MANUAL", log, out error))
            {
                return false;
            }

            if (!RestartService("w32time", log, out error))
            {
                return false;
            }

            RunTool("w32tm.exe", "/resync", log, out error);
            return true;
        }

        private static bool ResetNetwork(Action<WinUtilLogLine> log, out string error)
        {
            if (!RunTool("netsh.exe", "winsock reset", log, out error))
            {
                return false;
            }

            if (!RunTool("netsh.exe", "int ip reset", log, out error))
            {
                return false;
            }

            Report(log, "The network configuration was reset. Restart the computer for it to take effect.", WinUtilLogLevel.Warning);
            return true;
        }

        private static bool RepairSystem(Action<WinUtilLogLine> log, out string error)
        {
            error = null;

            int code = RunToolCode("chkdsk.exe", "/scan /perf", log, out error, 60 * 60 * 1000);
            if (code != 0 && code != 1 && code != 2 && code != 3)
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = "The disk check failed with exit code " + code + ".";
                }

                return false;
            }

            code = RunToolCode("sfc.exe", "/scannow", log, out error, 60 * 60 * 1000);
            if (code != 0)
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = "The system file check failed with exit code " + code + ".";
                }

                return false;
            }

            code = RunToolCode("dism.exe", "/online /cleanup-image /restorehealth", log, out error, 60 * 60 * 1000);
            if (code != 0 && code != 3010)
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = "The image repair failed with exit code " + code + ".";
                }

                return false;
            }

            return true;
        }

        private static bool EnableSshServer(Action<WinUtilLogLine> log, out string error)
        {
            error = null;

            int code = RunToolCode(
                "dism.exe",
                "/Online /Add-Capability /CapabilityName:OpenSSH.Server~~~~0.0.1.0",
                log,
                out error,
                60 * 60 * 1000);

            if (code != 0 && code != 3010)
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = "The OpenSSH Server capability could not be installed (exit code " + code + ").";
                }

                return false;
            }

            if (!SetAutomatic("sshd", log, out error) || !SetAutomatic("ssh-agent", log, out error))
            {
                return false;
            }

            RunTool("netsh.exe", "advfirewall firewall add rule name=\"OpenSSH Server (sshd)\" dir=in action=allow protocol=TCP localport=22", log, out error);
            Report(log, "The OpenSSH server is enabled. Add your public keys to the administrators_authorized_keys file in the ssh folder of ProgramData.", WinUtilLogLevel.Success);
            return true;
        }

        private static bool SetAutomatic(string serviceName, Action<WinUtilLogLine> log, out string error)
        {
            string serviceError;
            if (!WinUtilServices.SetStartupType(serviceName, "Automatic", out serviceError))
            {
                error = serviceError;
                Report(log, serviceError, WinUtilLogLevel.Warning);
                return false;
            }

            return StartService(serviceName, log, out error);
        }

        private static bool StartService(string serviceName, Action<WinUtilLogLine> log, out string error)
        {
            error = null;

            try
            {
                using (ServiceController service = new ServiceController(serviceName))
                {
                    if (service.Status == ServiceControllerStatus.Running)
                    {
                        return true;
                    }

                    service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = "The service " + serviceName + " could not be started. " + ex.Message;
                Report(log, error, WinUtilLogLevel.Error);
                return false;
            }
        }

        private static bool RestartService(string serviceName, Action<WinUtilLogLine> log, out string error)
        {
            error = null;

            try
            {
                using (ServiceController service = new ServiceController(serviceName))
                {
                    if (service.Status != ServiceControllerStatus.Stopped)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    }

                    service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = "The service " + serviceName + " could not be restarted. " + ex.Message;
                Report(log, error, WinUtilLogLevel.Error);
                return false;
            }
        }

        private static bool RunTool(string fileName, string arguments, Action<WinUtilLogLine> log, out string error)
        {
            int code = RunToolCode(fileName, arguments, log, out error, 30 * 60 * 1000);
            if (code != 0)
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = fileName + " ended with exit code " + code + ".";
                }

                Report(log, error, WinUtilLogLevel.Error);
                return false;
            }

            return true;
        }

        private static int RunToolCode(
            string fileName,
            string arguments,
            Action<WinUtilLogLine> log,
            out string error,
            int timeoutMilliseconds)
        {
            int code = WinUtilProcess.Run(
                fileName,
                arguments,
                delegate(string line) { Report(log, line, WinUtilLogLevel.Info); },
                out error,
                timeoutMilliseconds);

            if (code != 0 && !string.IsNullOrEmpty(error))
            {
                Report(log, error, WinUtilLogLevel.Error);
            }

            return code;
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
