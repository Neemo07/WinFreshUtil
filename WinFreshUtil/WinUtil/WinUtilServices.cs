using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinFreshUtil.WinUtil
{
    /// <summary>
    /// Changes the startup type of a Windows service without calling sc.exe.
    /// </summary>
    public static class WinUtilServices
    {
        private const uint ScManagerConnect = 0x0001;
        private const uint ServiceChangeConfig = 0x0002;
        private const uint ServiceQueryConfig = 0x0001;

        private const uint ServiceNoChange = 0xFFFFFFFF;
        private const uint ServiceAutoStart = 0x00000002;
        private const uint ServiceDemandStart = 0x00000003;
        private const uint ServiceDisabled = 0x00000004;
        private const uint ServiceBootStart = 0x00000000;
        private const uint ServiceSystemStart = 0x00000001;

        private const uint ServiceConfigDelayedAutoStartInfo = 0x00000003;

        /// <summary>
        /// The startup type that the service has now, in the words of the definitions.
        /// </summary>
        public static string ReadStartupType(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName))
            {
                return "Unknown";
            }

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\" + serviceName, false))
                {
                    if (key == null)
                    {
                        return "Missing";
                    }

                    object start = key.GetValue("Start");
                    int value = start == null ? -1 : Convert.ToInt32(start);

                    switch (value)
                    {
                        case 0:
                            return "Boot";
                        case 1:
                            return "System";
                        case 2:
                            object delayed = key.GetValue("DelayedAutostart");
                            bool isDelayed = delayed != null && Convert.ToInt32(delayed) != 0;
                            return isDelayed ? "AutomaticDelayedStart" : "Automatic";
                        case 3:
                            return "Manual";
                        case 4:
                            return "Disabled";
                        default:
                            return "Unknown";
                    }
                }
            }
            catch (Exception)
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Sets the startup type. Returns false when the service does not exist.
        /// </summary>
        public static bool SetStartupType(string serviceName, string startupType, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(serviceName))
            {
                error = "The service has no name.";
                return false;
            }

            uint start;
            bool delayed = false;

            if (!TryMapStartupType(startupType, out start, out delayed))
            {
                error = "The startup type " + startupType + " is not known.";
                return false;
            }

            IntPtr manager = IntPtr.Zero;
            IntPtr service = IntPtr.Zero;

            try
            {
                manager = OpenSCManager(null, null, ScManagerConnect);
                if (manager == IntPtr.Zero)
                {
                    error = "The service manager could not be opened: " + LastError();
                    return false;
                }

                service = OpenService(manager, serviceName, ServiceChangeConfig | ServiceQueryConfig);
                if (service == IntPtr.Zero)
                {
                    int code = Marshal.GetLastWin32Error();
                    if (code == 1060)
                    {
                        error = "The service " + serviceName + " does not exist on this computer.";
                    }
                    else
                    {
                        error = "The service " + serviceName + " could not be opened: " + LastError();
                    }

                    return false;
                }

                if (!ChangeServiceConfig(
                    service,
                    ServiceNoChange,
                    start,
                    ServiceNoChange,
                    null,
                    null,
                    IntPtr.Zero,
                    null,
                    null,
                    null,
                    null))
                {
                    error = "The startup type of " + serviceName + " could not be changed: " + LastError();
                    return false;
                }

                ServiceDelayedAutoStartInfo info = new ServiceDelayedAutoStartInfo
                {
                    DelayedAutostart = delayed ? 1 : 0
                };

                IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ServiceDelayedAutoStartInfo)));
                try
                {
                    Marshal.StructureToPtr(info, buffer, false);
                    if (!ChangeServiceConfig2(service, ServiceConfigDelayedAutoStartInfo, buffer))
                    {
                        error = "The delayed start of " + serviceName + " could not be changed: " + LastError();
                        return false;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "The service " + serviceName + " could not be changed. " + ex.Message;
                return false;
            }
            finally
            {
                if (service != IntPtr.Zero)
                {
                    CloseServiceHandle(service);
                }

                if (manager != IntPtr.Zero)
                {
                    CloseServiceHandle(manager);
                }
            }
        }

        /// <summary>
        /// True when the current startup type is the one the definition expects.
        /// </summary>
        public static bool Matches(string serviceName, string expected)
        {
            if (string.IsNullOrEmpty(expected))
            {
                return false;
            }

            return string.Equals(ReadStartupType(serviceName), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryMapStartupType(string startupType, out uint start, out bool delayed)
        {
            delayed = false;

            switch ((startupType ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "AUTOMATIC":
                    start = ServiceAutoStart;
                    return true;
                case "AUTOMATICDELAYEDSTART":
                    start = ServiceAutoStart;
                    delayed = true;
                    return true;
                case "MANUAL":
                    start = ServiceDemandStart;
                    return true;
                case "DISABLED":
                    start = ServiceDisabled;
                    return true;
                case "BOOT":
                    start = ServiceBootStart;
                    return true;
                case "SYSTEM":
                    start = ServiceSystemStart;
                    return true;
                default:
                    start = ServiceNoChange;
                    return false;
            }
        }

        private static string LastError()
        {
            int code = Marshal.GetLastWin32Error();
            return new Win32Exception(code).Message + " (" + code + ")";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceDelayedAutoStartInfo
        {
            public int DelayedAutostart;
        }

        [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManager(string machineName, string databaseName, uint access);

        [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenService(IntPtr manager, string serviceName, uint access);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ChangeServiceConfig(
            IntPtr service,
            uint serviceType,
            uint startType,
            uint errorControl,
            string binaryPathName,
            string loadOrderGroup,
            IntPtr tagId,
            string dependencies,
            string serviceStartName,
            string password,
            string displayName);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ChangeServiceConfig2(
            IntPtr service,
            uint infoLevel,
            IntPtr info);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr handle);
    }
}
