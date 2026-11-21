using System.Collections.Generic;

namespace WinFreshUtil.Models
{
    /// <summary>
    /// A device that Windows reports a problem with.
    /// </summary>
    public sealed class ProblemDevice
    {
        public string Name { get; set; }

        public string DeviceId { get; set; }

        public string[] HardwareIds { get; set; }

        /// <summary>
        /// The ids the device is compatible with. A driver often fits one of these
        /// instead of the id of the device itself.
        /// </summary>
        public string[] CompatibleIds { get; set; }

        public string Manufacturer { get; set; }

        public int ConfigManagerErrorCode { get; set; }

        /// <summary>The error code in a readable form.</summary>
        public string ErrorDescription { get; set; }

        /// <summary>The hardware id that is shown in the list.</summary>
        public string HardwareIdText
        {
            get
            {
                if (HardwareIds == null || HardwareIds.Length == 0)
                {
                    return DeviceId ?? string.Empty;
                }

                return HardwareIds[0];
            }
        }

        /// <summary>The text that is used to search the Microsoft Update Catalog.</summary>
        public string CatalogSearchText
        {
            get
            {
                return HardwareIdText;
            }
        }
    }

    /// <summary>
    /// The result of a scan for devices with driver problems.
    /// </summary>
    public sealed class DriverScanResult
    {
        public DriverScanResult()
        {
            Devices = new List<ProblemDevice>();
            VideoControllers = new List<string>();
        }

        /// <summary>The devices that have a problem.</summary>
        public IList<ProblemDevice> Devices { get; private set; }

        /// <summary>The manufacturer of the computer.</summary>
        public string ComputerManufacturer { get; set; }

        /// <summary>The model of the computer.</summary>
        public string ComputerModel { get; set; }

        /// <summary>The names of the graphics adapters.</summary>
        public IList<string> VideoControllers { get; private set; }

        /// <summary>Set when the scan could not be completed.</summary>
        public string Error { get; set; }
    }
}
