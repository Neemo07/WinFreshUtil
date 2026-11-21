using System;
using System.IO;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Finds the package managers that are on the computer.
    /// </summary>
    public static class PackageManagerLocator
    {
        /// <summary>
        /// True when the package manager of Windows is on the computer.
        /// </summary>
        public static bool IsWingetAvailable()
        {
            return FindWinget() != null;
        }

        /// <summary>
        /// True when Chocolatey is on the computer.
        /// </summary>
        public static bool IsChocolateyAvailable()
        {
            return FindChoco() != null;
        }

        /// <summary>
        /// The address of the package manager of Windows, or null when it is not
        /// installed.
        /// </summary>
        public static string FindWinget()
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WindowsApps", "winget.exe");

                if (File.Exists(path))
                {
                    return path;
                }
            }
            catch (Exception)
            {
                // The search below is used then.
            }

            string system = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(system))
            {
                string systemPath = Path.Combine(system, "System32", "winget.exe");
                if (File.Exists(systemPath))
                {
                    return systemPath;
                }
            }

            return null;
        }

        /// <summary>
        /// The address of Chocolatey, or null when it is not installed.
        /// </summary>
        public static string FindChoco()
        {
            string[] folders =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\chocolatey\bin",
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\chocolatey\bin"
            };

            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder))
                {
                    continue;
                }

                try
                {
                    string path = Path.Combine(folder.TrimEnd('\\'), "choco.exe");
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
                catch (Exception)
                {
                    // The next folder is used then.
                }
            }

            return null;
        }
    }
}
