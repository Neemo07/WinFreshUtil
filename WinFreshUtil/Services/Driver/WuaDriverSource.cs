using System;
using System.Collections.Generic;
using System.Globalization;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services.Driver
{
    /// <summary>
    /// The drivers that Windows Update has for a device.
    ///
    /// The search is made through the update agent of Windows. The agent looks at the
    /// hardware of the computer itself, so the answer is short. The driver is then
    /// found again by its id, because the agent cannot hand over a driver that it has
    /// not found itself.
    /// </summary>
    public sealed class WuaDriverSource : IDriverSource
    {
        public string Name
        {
            get { return "Windows Update"; }
        }

        /// <summary>
        /// Looks for the drivers of a device. Runs on a background thread.
        /// </summary>
        public IList<DriverCandidate> Find(ProblemDevice device, Action<string> progress)
        {
            List<DriverCandidate> found = new List<DriverCandidate>();

            if (device == null)
            {
                return found;
            }

            Report(progress, "Looking for a driver in Windows Update...");

            object session = WuaSession.CreateSession();

            if (session == null)
            {
                Report(progress, "The update agent of Windows is not on this computer.");
                return found;
            }

            try
            {
                object searcher = WuaSession.Call(session, "CreateUpdateSearcher");
                WuaSession.SetProperty(searcher, "Online", true);

                Report(progress, "Windows Update is searching for drivers. This can take a while...");

                object result = WuaSession.Call(searcher, "Search", "IsInstalled=0 AND Type='Driver'");

                object updates = WuaSession.GetProperty(result, "Updates");
                int count = Convert.ToInt32(WuaSession.GetProperty(updates, "Count"), CultureInfo.InvariantCulture);

                Report(progress, "Windows Update answered with " + count + " driver(s).");

                for (int index = 0; index < count; index++)
                {
                    object update = WuaSession.GetIndexed(updates, "Item", index);

                    string hardwareId = WuaSession.GetText(update, "DriverHardwareID");

                    if (!DriverLookup.Matches(device, hardwareId))
                    {
                        continue;
                    }

                    found.Add(ReadUpdate(update, hardwareId));
                }
            }
            catch (Exception ex)
            {
                LogService.Error("The search in Windows Update failed.", ex);
                Report(progress, "Windows Update could not be asked. " + ex.Message);
            }

            return found;
        }

        /// <summary>
        /// Downloads and installs a driver. Runs on a background thread with
        /// administrator rights.
        /// </summary>
        public bool Install(DriverCandidate candidate, DriverInstallContext context)
        {
            if (candidate == null)
            {
                return false;
            }

            context.Write("Windows Update is searched again for the driver \"" + candidate.Name + "\"...");
            context.Write("This search takes about half a minute. The update agent of Windows cannot hand over a driver that it has not found itself.");

            if (context.StopIfAsked("The installation was stopped."))
            {
                return false;
            }

            object session = WuaSession.CreateSession();

            if (session == null)
            {
                context.Write("The update agent of Windows is not on this computer.");
                return false;
            }

            object update = null;

            try
            {
                object searcher = WuaSession.Call(session, "CreateUpdateSearcher");
                WuaSession.SetProperty(searcher, "Online", true);

                object result = WuaSession.Call(searcher, "Search", "IsInstalled=0 AND Type='Driver'");

                object updates = WuaSession.GetProperty(result, "Updates");
                int count = Convert.ToInt32(WuaSession.GetProperty(updates, "Count"), CultureInfo.InvariantCulture);

                for (int index = 0; index < count; index++)
                {
                    object candidateUpdate = WuaSession.GetIndexed(updates, "Item", index);
                    object identity = WuaSession.GetProperty(candidateUpdate, "Identity");
                    string candidateId = identity == null ? string.Empty : WuaSession.GetText(identity, "UpdateID");

                    if (!string.IsNullOrEmpty(candidate.UpdateId) &&
                        string.Equals(candidateId, candidate.UpdateId, StringComparison.OrdinalIgnoreCase))
                    {
                        update = candidateUpdate;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error("The search in Windows Update failed.", ex);
                context.Write("Windows Update could not be asked. " + ex.Message);
                return false;
            }

            if (update == null)
            {
                context.Write("Windows Update does not know the driver any more.");
                return false;
            }

            object collection = WuaSession.CreateCollection();

            if (collection == null)
            {
                context.Write("The list of updates could not be made.");
                return false;
            }

            WuaSession.Call(collection, "Add", update);

            context.Write("The driver is downloaded. This can take a few minutes...");

            if (context.StopIfAsked("The installation was stopped."))
            {
                return false;
            }

            try
            {
                object downloader = WuaSession.Call(session, "CreateUpdateDownloader");
                WuaSession.SetProperty(downloader, "Updates", collection);
                WuaSession.Call(downloader, "Download");
            }
            catch (Exception ex)
            {
                LogService.Error("The download of the driver failed.", ex);
                context.Write("The driver could not be downloaded. " + ex.Message);
                return false;
            }

            context.Write("The driver is installed...");

            if (context.StopIfAsked("The installation was stopped."))
            {
                return false;
            }

            try
            {
                object installer = WuaSession.Call(session, "CreateUpdateInstaller");
                WuaSession.SetProperty(installer, "Updates", collection);

                object installResult = WuaSession.Call(installer, "Install");

                bool reboot = false;
                try
                {
                    reboot = Convert.ToBoolean(WuaSession.GetProperty(installResult, "RebootRequired"), CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    // The flag of the restart is only a note.
                }

                int resultCode = 0;
                try
                {
                    resultCode = Convert.ToInt32(WuaSession.GetProperty(installResult, "ResultCode"), CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    // The code of the result is only a note.
                }

                if (resultCode != 0 && resultCode != 2 && resultCode != 3)
                {
                    context.Write("Windows Update reported the code " + resultCode + ".");
                }

                context.Write(reboot
                    ? "The driver was installed. Windows has to be restarted to use it."
                    : "The driver was installed.");

                return true;
            }
            catch (Exception ex)
            {
                LogService.Error("The installation of the driver failed.", ex);
                context.Write("The driver could not be installed. " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Reads the driver out of an update of the update agent of Windows.
        /// </summary>
        private static DriverCandidate ReadUpdate(object update, string hardwareId)
        {
            string title = WuaSession.GetText(update, "Title");
            string manufacturer = WuaSession.GetText(update, "DriverManufacturer");
            string model = WuaSession.GetText(update, "DriverModel");

            string version = string.Empty;
            try
            {
                object date = WuaSession.GetProperty(update, "DriverVerDate");

                if (date != null)
                {
                    DateTime value;

                    if (date is DateTime)
                    {
                        value = (DateTime)date;
                    }
                    else
                    {
                        DateTime.TryParse(WuaSession.AsString(date), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
                    }

                    if (value != default(DateTime))
                    {
                        version = value.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
                    }
                }
            }
            catch (Exception)
            {
                // The date of the driver is only used for the list.
            }

            string updateId = string.Empty;
            try
            {
                object identity = WuaSession.GetProperty(update, "Identity");

                if (identity != null)
                {
                    updateId = WuaSession.GetText(identity, "UpdateID");
                }
            }
            catch (Exception)
            {
                // Without the identity the driver cannot be found again.
            }

            return new DriverCandidate
            {
                Name = string.IsNullOrEmpty(title) ? (manufacturer + " " + model).Trim() : title,
                Version = DriverLookup.ExtractVersion(title),
                Released = version,
                Source = "Windows Update",
                SourceUrl = "the update agent of Windows (Microsoft.Update.Session)",
                HardwareId = hardwareId,
                UpdateId = updateId,
                Stage = DriverStage.WindowsUpdate,
                Size = ReadSize(update)
            };
        }

        private static long ReadSize(object update)
        {
            try
            {
                return Convert.ToInt64(WuaSession.GetProperty(update, "MaxDownloadSize"), CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static void Report(Action<string> progress, string text)
        {
            if (progress != null && !string.IsNullOrEmpty(text))
            {
                progress(text);
            }
        }

    }
}
