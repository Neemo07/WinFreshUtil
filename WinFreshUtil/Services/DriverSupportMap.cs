using System;
using System.Text;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Finds the official support page of the manufacturer of a device.
    /// </summary>
    public static class DriverSupportMap
    {
        /// <summary>
        /// Returns the support page of the vendor for the device, or the search page of
        /// the Microsoft Update Catalog when the vendor is not known.
        /// </summary>
        public static string GetSupportUrl(ProblemDevice device, string computerManufacturer)
        {
            string vendor = FindVendor(device.Manufacturer) ?? FindVendor(computerManufacturer);

            if (vendor != null)
            {
                return vendor;
            }

            return GetCatalogUrl(device);
        }

        /// <summary>
        /// Returns the search page of the Microsoft Update Catalog for the hardware id.
        /// </summary>
        public static string GetCatalogUrl(ProblemDevice device)
        {
            string search = device == null ? string.Empty : device.CatalogSearchText;

            if (string.IsNullOrEmpty(search))
            {
                return "https://www.catalog.update.microsoft.com/Search.aspx?q=";
            }

            return "https://www.catalog.update.microsoft.com/Search.aspx?q=" +
                   Uri.EscapeDataString(search);
        }

        /// <summary>
        /// Returns the name of the known vendor, or null when the name is not known.
        /// </summary>
        private static string FindVendor(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            string lower = name.ToLowerInvariant();

            if (Contains(lower, "intel"))
            {
                return "https://www.intel.com/content/www/us/en/download-center/home.html";
            }

            if (Contains(lower, "amd") || Contains(lower, "advanced micro devices") ||
                Contains(lower, "ati ") || lower.Contains("ati technologies"))
            {
                return "https://www.amd.com/en/support.html";
            }

            if (Contains(lower, "nvidia"))
            {
                return "https://www.nvidia.com/Download/index.aspx";
            }

            if (Contains(lower, "realtek"))
            {
                return "https://www.realtek.com/Download/List?cate_id=584";
            }

            if (Contains(lower, "dell"))
            {
                return "https://www.dell.com/support/home/en-us";
            }

            if (Contains(lower, "hp ") || Contains(lower, "hewlett"))
            {
                return "https://support.hp.com/drivers";
            }

            if (Contains(lower, "lenovo"))
            {
                return "https://pcsupport.lenovo.com";
            }

            if (Contains(lower, "asus"))
            {
                return "https://www.asus.com/support/";
            }

            if (Contains(lower, "acer"))
            {
                return "https://www.acer.com/us-en/support";
            }

            if (Contains(lower, "msi") || Contains(lower, "micro-star"))
            {
                return "https://www.msi.com/support";
            }

            return null;
        }

        private static bool Contains(string text, string part)
        {
            return text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
