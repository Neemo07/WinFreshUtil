using System;
using System.Collections.Generic;

namespace WinFreshUtil.Models
{
    /// <summary>
    /// Activation state of a licensed product.
    /// The numeric values match the WMI SoftwareLicensingProduct LicenseStatus property.
    /// </summary>
    public enum LicenseState
    {
        Unlicensed = 0,
        Licensed = 1,
        OutOfBoxGrace = 2,
        OutOfToleranceGrace = 3,
        NonGenuineGrace = 4,
        Notification = 5,
        ExtendedGrace = 6,
        Unknown = -1
    }

    /// <summary>
    /// Human readable text for a <see cref="LicenseState"/>.
    /// </summary>
    public static class LicenseStateText
    {
        public static string Describe(LicenseState state)
        {
            switch (state)
            {
                case LicenseState.Licensed:
                    return "Activated";
                case LicenseState.OutOfBoxGrace:
                    return "Activated (out-of-box grace period)";
                case LicenseState.OutOfToleranceGrace:
                    return "Activated (out-of-tolerance grace period)";
                case LicenseState.ExtendedGrace:
                    return "Activated (extended grace period)";
                case LicenseState.Unlicensed:
                    return "Not activated";
                case LicenseState.Notification:
                    return "Not activated (notification mode)";
                case LicenseState.NonGenuineGrace:
                    return "Not genuine (non-genuine grace period)";
                default:
                    return "Unknown";
            }
        }

        /// <summary>
        /// True when the product is usable under its current license.
        /// </summary>
        public static bool IsUsable(LicenseState state)
        {
            return state == LicenseState.Licensed
                   || state == LicenseState.OutOfBoxGrace
                   || state == LicenseState.OutOfToleranceGrace
                   || state == LicenseState.ExtendedGrace;
        }
    }

    /// <summary>
    /// A single licensed product found on the machine.
    /// </summary>
    public sealed class LicenseEntry
    {
        public string ProductName { get; set; }
        public string Description { get; set; }
        public string PartialProductKey { get; set; }
        public LicenseState State { get; set; }
        public int? GraceDays { get; set; }

        public string StateText
        {
            get { return LicenseStateText.Describe(State); }
        }

        public bool IsUsable
        {
            get { return LicenseStateText.IsUsable(State); }
        }
    }

    /// <summary>
    /// Result of a license inspection.
    /// </summary>
    public sealed class LicenseSnapshot
    {
        public LicenseSnapshot()
        {
            WindowsProducts = new List<LicenseEntry>();
            OfficeProducts = new List<LicenseEntry>();
            Notes = new List<string>();
        }

        public IList<LicenseEntry> WindowsProducts { get; private set; }
        public IList<LicenseEntry> OfficeProducts { get; private set; }
        public IList<string> Notes { get; private set; }

        /// <summary>True when the firmware carries an OEM product key.</summary>
        public bool FirmwareKeyPresent { get; set; }

        /// <summary>How the Office products were detected, for display purposes.</summary>
        public string OfficeDetectionSource { get; set; }

        /// <summary>Set when the inspection could not be completed.</summary>
        public string Error { get; set; }
    }
}
