using System;
using System.Collections.Generic;

namespace WinFreshUtil.Services.Driver
{
    /// <summary>
    /// The place a driver comes from.
    /// </summary>
    public enum DriverStage
    {
        WindowsUpdate,
        UpdateCatalog,
        OemTool
    }

    /// <summary>
    /// One driver that can be installed.
    /// </summary>
    public sealed class DriverCandidate
    {
        /// <summary>The name of the driver, as Windows shows it.</summary>
        public string Name { get; set; }

        /// <summary>The version of the driver, when it is known.</summary>
        public string Version { get; set; }

        /// <summary>The day the driver was made, when it is known.</summary>
        public string Released { get; set; }

        /// <summary>Where the driver comes from, in words.</summary>
        public string Source { get; set; }

        /// <summary>The place the driver is downloaded from.</summary>
        public string SourceUrl { get; set; }

        /// <summary>The hardware id the driver belongs to.</summary>
        public string HardwareId { get; set; }

        /// <summary>The identity of the update, used to find it again.</summary>
        public string UpdateId { get; set; }

        /// <summary>How big the driver is, in bytes.</summary>
        public long Size { get; set; }

        public DriverStage Stage { get; set; }
    }

    /// <summary>
    /// The official tool of a manufacturer, which is used when no driver can be
    /// found in Windows Update or in the catalog.
    /// </summary>
    public sealed class OemTool
    {
        public string Name { get; set; }
        public string WingetId { get; set; }
        public string Page { get; set; }
    }

    /// <summary>
    /// What a driver source needs while it installs a driver.
    /// </summary>
    public sealed class DriverInstallContext
    {
        private readonly Action<string> _write;
        private readonly Func<bool> _isStopRequested;

        public DriverInstallContext(Action<string> write, Func<bool> isStopRequested)
        {
            _write = write;
            _isStopRequested = isStopRequested;
        }

        /// <summary>
        /// Writes one step of the work, so that the user sees where it is.
        /// </summary>
        public void Write(string text)
        {
            if (_write != null && !string.IsNullOrEmpty(text))
            {
                _write(text);
            }
        }

        /// <summary>
        /// True when the user asked for the work to stop.
        /// </summary>
        public bool IsStopRequested()
        {
            return _isStopRequested != null && _isStopRequested();
        }

        /// <summary>
        /// Writes a step and says that the work was stopped, when the user asked for
        /// it. Returns true when the work has to stop.
        /// </summary>
        public bool StopIfAsked(string message)
        {
            if (!IsStopRequested())
            {
                return false;
            }

            Write(message);

            return true;
        }
    }
}
