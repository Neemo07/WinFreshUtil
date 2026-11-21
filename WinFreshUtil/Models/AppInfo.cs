using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace WinFreshUtil.Models
{
    /// <summary>
    /// Static information about the running application, used by the About page.
    /// </summary>
    public static class AppInfo
    {
        public static string Name
        {
            get { return "WinFreshUtil"; }
        }

        public static string Description
        {
            get { return "A tiny post-install utility for fresh Windows 10/11 machines."; }
        }

        public static string Framework
        {
            get { return ".NET Framework 4.8"; }
        }

        public static string Version
        {
            get
            {
                Version version = GetVersion();
                return version == null ? "unknown" : version.Major + "." + version.Minor + "." + version.Build;
            }
        }

        public static string FullVersion
        {
            get
            {
                Version version = GetVersion();
                return version == null ? "unknown" : version.ToString();
            }
        }

        /// <summary>
        /// The date the running build was made.
        /// </summary>
        public static string Build
        {
            get
            {
                try
                {
                    DateTime build = new FileInfo(GetAssemblyPath()).LastWriteTime;
                    return build.ToString("yyyy-MM-dd HH:mm");
                }
                catch (Exception)
                {
                    return "unknown";
                }
            }
        }

        /// <summary>
        /// A short description of the operating system. The version is read from the
        /// registry, because .NET Framework reports the version it was compiled for
        /// unless the application declares the systems it supports.
        /// </summary>
        public static string OperatingSystem
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                               @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    {
                        if (key == null)
                        {
                            return "unknown";
                        }

                        string product = Convert.ToString(key.GetValue("ProductName", "Windows")).Trim();
                        string release = Convert.ToString(key.GetValue("DisplayVersion", "")).Trim();

                        // CurrentVersion still reports 6.3 on Windows 10, so the build is
                        // put together from the values that are correct.
                        string major = Convert.ToString(key.GetValue("CurrentMajorVersionNumber", "")).Trim();
                        string minor = Convert.ToString(key.GetValue("CurrentMinorVersionNumber", "")).Trim();
                        string build = Convert.ToString(key.GetValue("CurrentBuildNumber", "")).Trim();

                        string version = major.Length > 0 && minor.Length > 0 && build.Length > 0
                            ? major + "." + minor + "." + build
                            : Convert.ToString(key.GetValue("CurrentVersion", "")).Trim();

                        string text = product;
                        if (version.Length > 0)
                        {
                            text += " (" + version;
                            if (release.Length > 0)
                            {
                                text += ", " + release;
                            }

                            text += ")";
                        }

                        return text;
                    }
                }
                catch (Exception)
                {
                    return "unknown";
                }
            }
        }

        /// <summary>
        /// True when the application runs with administrator rights.
        /// </summary>
        public static string IsElevated
        {
            get { return Services.AdminService.IsElevated() ? "yes" : "no"; }
        }

        private static Version GetVersion()
        {
            try
            {
                return Assembly.GetExecutingAssembly().GetName().Version;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string GetAssemblyPath()
        {
            try
            {
                return Assembly.GetExecutingAssembly().Location;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
