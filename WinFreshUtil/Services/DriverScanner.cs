using System;
using System.Collections.Generic;
using System.Management;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Reads the devices with a driver problem from Windows. The queries use WMI and are
    /// slow, so they have to run on a background thread.
    /// </summary>
    public static class DriverScanner
    {
        /// <summary>
        /// Scans the machine for devices that Windows reports a problem with.
        /// </summary>
        public static DriverScanResult Scan()
        {
            DriverScanResult result = new DriverScanResult();

            try
            {
                ReadComputerSystem(result);
                ReadVideoControllers(result);
                ReadProblemDevices(result);

                LogService.Info("Driver scan finished, " + result.Devices.Count + " device(s) with a problem.");
            }
            catch (Exception ex)
            {
                result.Error = "The devices could not be read. " + ex.Message;
                LogService.Error("The driver scan failed.", ex);
            }

            return result;
        }

        private static void ReadComputerSystem(DriverScanResult result)
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                       "SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
            using (ManagementObjectCollection computers = searcher.Get())
            {
                foreach (ManagementObject computer in computers)
                {
                    result.ComputerManufacturer = GetString(computer, "Manufacturer");
                    result.ComputerModel = GetString(computer, "Model");
                    break;
                }
            }
        }

        private static void ReadVideoControllers(DriverScanResult result)
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                       "SELECT Name FROM Win32_VideoController"))
            using (ManagementObjectCollection adapters = searcher.Get())
            {
                foreach (ManagementObject adapter in adapters)
                {
                    string name = GetString(adapter, "Name");
                    if (!string.IsNullOrEmpty(name))
                    {
                        result.VideoControllers.Add(name);
                    }
                }
            }
        }

        private static void ReadProblemDevices(DriverScanResult result)
        {
            const string query =
                "SELECT Name, DeviceID, HardwareID, CompatibleID, Manufacturer, ConfigManagerErrorCode " +
                "FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0";

            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
            using (ManagementObjectCollection devices = searcher.Get())
            {
                foreach (ManagementObject device in devices)
                {
                    int errorCode = GetInt32(device, "ConfigManagerErrorCode");

                    result.Devices.Add(new ProblemDevice
                    {
                        Name = GetString(device, "Name"),
                        DeviceId = GetString(device, "DeviceID"),
                        HardwareIds = GetStringArray(device, "HardwareID"),
                        CompatibleIds = GetStringArray(device, "CompatibleID"),
                        Manufacturer = GetString(device, "Manufacturer"),
                        ConfigManagerErrorCode = errorCode,
                        ErrorDescription = DescribeErrorCode(errorCode)
                    });
                }
            }

            // The devices with the worst problems are the interesting ones.
            List<ProblemDevice> sorted = new List<ProblemDevice>(result.Devices);
            sorted.Sort(delegate(ProblemDevice left, ProblemDevice right)
            {
                return right.ConfigManagerErrorCode.CompareTo(left.ConfigManagerErrorCode);
            });

            result.Devices.Clear();
            foreach (ProblemDevice device in sorted)
            {
                result.Devices.Add(device);
            }
        }

        /// <summary>
        /// Turns the Windows error code of a device into a readable text.
        /// </summary>
        public static string DescribeErrorCode(int code)
        {
            switch (code)
            {
                case 1:
                    return "The device is not present. Windows cannot find it on the computer.";
                case 2:
                    return "Windows cannot configure this device.";
                case 3:
                    return "The driver for this device is missing.";
                case 4:
                    return "The device is disabled. This is often reported for hardware that is not present, such as a card reader or a slot that is empty.";
                case 5:
                    return "Windows cannot load the driver for this device.";
                case 6:
                    return "Windows cannot load the driver. The driver may be damaged or missing.";
                case 7:
                    return "Windows cannot load the driver for this device.";
                case 8:
                    return "The device needs a restart to work.";
                case 9:
                    return "The driver is out of date. A newer version is installed by Windows Update.";
                case 10:
                    return "This device cannot start. The driver does not work with this hardware.";
                case 12:
                    return "Windows cannot start this device. There is not enough memory or resources for it.";
                case 22:
                    return "The device is disabled.";
                case 28:
                    return "The driver for this device is not installed.";
                case 31:
                    return "The device does not work. There is not enough memory or resources for it.";
                case 43:
                    return "Windows has stopped this device because it reported a problem. This is the most common hardware error.";
                case 45:
                    return "The device is not connected to the computer.";
                default:
                    return "Windows reported a problem with this device.";
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

        private static int GetInt32(ManagementObject obj, string property)
        {
            try
            {
                object value = obj[property];
                return value == null ? 0 : Convert.ToInt32(value);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static string[] GetStringArray(ManagementObject obj, string property)
        {
            try
            {
                string[] values = obj[property] as string[];
                return values ?? new string[0];
            }
            catch (Exception)
            {
                return new string[0];
            }
        }
    }
}
