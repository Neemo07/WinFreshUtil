using System.Collections.Generic;

namespace WinFreshUtil.Models
{
    /// <summary>
    /// One entry of a Microsoft Activation Scripts menu. The entries are read from the
    /// script itself, so the list always matches the version that is run.
    /// </summary>
    public sealed class MasOption
    {
        /// <summary>The key that is pressed to pick the entry, for example "1" or "E".</summary>
        public string Key { get; set; }

        /// <summary>The name of the function, for example "HWID".</summary>
        public string Label { get; set; }

        /// <summary>The short description, for example "Windows".</summary>
        public string Description { get; set; }

        /// <summary>True for the entry that leaves the script.</summary>
        public bool IsExit { get; set; }
    }

    /// <summary>
    /// The functions that the Microsoft Activation Scripts offer.
    /// </summary>
    public sealed class MasMenu
    {
        public MasMenu()
        {
            Options = new List<MasOption>();
        }

        public IList<MasOption> Options { get; private set; }

        /// <summary>Set when the menu could not be read.</summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// Result of running a function of the Microsoft Activation Scripts.
    /// </summary>
    public sealed class ActivationOutcome
    {
        public bool Success { get; set; }

        public int? ExitCode { get; set; }

        public string Message { get; set; }
    }
}
