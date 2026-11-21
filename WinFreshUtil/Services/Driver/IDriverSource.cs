using System;
using System.Collections.Generic;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services.Driver
{
    /// <summary>
    /// A place where a driver can be looked for and installed.
    /// </summary>
    public interface IDriverSource
    {
        /// <summary>
        /// The name of the place, for the window and for the log.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Looks for the drivers of a device. Runs on a background thread.
        /// </summary>
        IList<DriverCandidate> Find(ProblemDevice device, Action<string> progress);

        /// <summary>
        /// Installs a driver that was found here. Runs on a background thread with
        /// administrator rights.
        /// </summary>
        bool Install(DriverCandidate candidate, DriverInstallContext context);
    }
}
