using System;
using System.IO;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// The folders that WinFreshUtil uses on the computer.
    /// </summary>
    public static class AppPaths
    {
        /// <summary>
        /// The folder of the application. It is deliberately not inside the temporary
        /// folder, because the activation scripts refuse to run from there.
        /// </summary>
        public static string GetDataFolder()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root) || IsInsideTempFolder(root))
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            if (string.IsNullOrEmpty(root))
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return Path.Combine(root, "WinFreshUtil");
        }

        /// <summary>
        /// The folder the activation scripts are kept in. The scripts are taken out of
        /// the application and an update stores the latest ones here.
        /// </summary>
        public static string GetMasFolder()
        {
            return Path.Combine(GetDataFolder(), "mas");
        }

        /// <summary>
        /// The file that tells a new instance of the application what it should do.
        /// </summary>
        public static string GetPendingActionPath()
        {
            return Path.Combine(GetDataFolder(), "pending.action");
        }

        private static bool IsInsideTempFolder(string path)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(userProfile))
            {
                return false;
            }

            string temp = Path.Combine(userProfile, "AppData", "Local", "Temp");
            return path.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
        }
    }
}
