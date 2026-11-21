using System;
using System.Diagnostics;
using System.Security.Principal;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Helpers for working with administrator rights.
    /// </summary>
    public static class AdminService
    {
        /// <summary>
        /// True when the current process runs with administrator rights.
        /// </summary>
        public static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Starts the application again with administrator rights. The action that the new
        /// instance should carry out is stored in a file, because the elevated instance
        /// does not get the command line.
        /// </summary>
        public static bool RelaunchAsAdmin(string action, out string error)
        {
            error = null;

            try
            {
                if (action != null)
                {
                    StartupAction.Set(action);
                }

                string path = Process.GetCurrentProcess().MainModule.FileName;

                ProcessStartInfo startInfo = new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(startInfo);
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)
                {
                    // The user did not allow the elevation. The action is taken away,
                    // so that a later start of the application does not carry it out
                    // without the user asking for it.
                    StartupAction.Clear(action);

                    error = "The elevation request was declined.";
                    return false;
                }

                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
