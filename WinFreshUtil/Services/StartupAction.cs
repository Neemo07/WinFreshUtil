using System;
using System.IO;
using System.Text;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// A small file that tells a newly started instance of the application what it
    /// should do. It is used when the application starts itself again with administrator
    /// rights, because the elevated instance does not get the command line.
    /// </summary>
    public static class StartupAction
    {
        /// <summary>Work through a request of the activation helper.</summary>
        public const string MasHelper = "mas-helper";

        /// <summary>Work through a request of the driver installer.</summary>
        public const string DriverInstaller = "driver-installer";

        /// <summary>Work through a request of RapidOS.</summary>
        public const string RapidOs = "rapidos";

        /// <summary>Start WinUtil.</summary>
        public const string WinUtil = "winutil";

        private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Stores the action for the next instance of the application.
        /// </summary>
        public static void Set(string action)
        {
            try
            {
                string folder = AppPaths.GetDataFolder();
                Directory.CreateDirectory(folder);

                File.WriteAllText(
                    AppPaths.GetPendingActionPath(),
                    action + "|" + DateTime.Now.ToString("o"),
                    Encoding.ASCII);
            }
            catch (Exception)
            {
                // Without the file the next instance simply starts normally.
            }
        }

        /// <summary>
        /// Removes the action for the next instance. It is used when the user did not
        /// allow the elevation, so that the action is not carried out by a later start
        /// of the application.
        /// </summary>
        public static void Clear(string action)
        {
            if (string.IsNullOrEmpty(action))
            {
                return;
            }

            try
            {
                string path = AppPaths.GetPendingActionPath();

                if (!File.Exists(path))
                {
                    return;
                }

                string[] parts = File.ReadAllText(path).Trim().Split('|');

                if (parts.Length > 0 && string.Equals(parts[0], action, StringComparison.Ordinal))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // The file is only used to tell the next instance what to do.
            }
        }

        /// <summary>
        /// Reads the action and removes the file, so that it is only used once.
        /// Returns null when there is nothing to do.
        /// </summary>
        public static string Take()
        {
            string path = AppPaths.GetPendingActionPath();

            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                string content = File.ReadAllText(path).Trim();
                File.Delete(path);

                string[] parts = content.Split('|');
                if (parts.Length == 0 || parts[0].Length == 0)
                {
                    return null;
                }

                // A file that is too old comes from an instance that never started.
                if (parts.Length > 1)
                {
                    DateTime written;
                    if (DateTime.TryParse(parts[1], out written) && DateTime.Now - written > MaxAge)
                    {
                        return null;
                    }
                }

                return parts[0];
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
