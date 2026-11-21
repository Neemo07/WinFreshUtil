using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services.Driver
{
    /// <summary>
    /// The things that every place with drivers needs: whether a driver belongs to a
    /// device, and the version out of the name of a driver.
    /// </summary>
    public static class DriverLookup
    {
        /// <summary>
        /// True when the hardware id of a driver belongs to the device. The ids of the
        /// device and the ids it is compatible with are both used, because a driver
        /// often fits one of them and not the other.
        /// </summary>
        public static bool Matches(ProblemDevice device, string driverHardwareId)
        {
            if (device == null || string.IsNullOrEmpty(driverHardwareId))
            {
                return false;
            }

            foreach (string part in driverHardwareId.Split(';'))
            {
                string wanted = part.Trim();

                if (wanted.Length == 0)
                {
                    continue;
                }

                foreach (string id in GetAllIds(device))
                {
                    if (string.Equals(id.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The hardware ids of the device, both the ones of the device itself and the
        /// ones it is compatible with.
        /// </summary>
        public static IEnumerable<string> GetAllIds(ProblemDevice device)
        {
            if (device == null)
            {
                yield break;
            }

            if (device.HardwareIds != null)
            {
                foreach (string id in device.HardwareIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        yield return id;
                    }
                }
            }

            if (device.CompatibleIds != null)
            {
                foreach (string id in device.CompatibleIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        yield return id;
                    }
                }
            }
        }

        /// <summary>
        /// The version of a driver is the last part of its name, for example
        /// "Intel Corporation - Display - 20.19.15.5166".
        /// </summary>
        public static string ExtractVersion(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return string.Empty;
            }

            Match match = Regex.Match(title, @"(\d+(?:\.\d+)+)\s*$");

            return match.Success ? match.Groups[1].Value : string.Empty;
        }
    }
}
