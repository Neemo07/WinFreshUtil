using System;
using System.Collections.Generic;

namespace WinFreshUtil.Models
{
    /// <summary>
    /// The command that runs one of the functions of the Microsoft Activation Scripts.
    /// The functions are found by the name that the script itself prints.
    /// </summary>
    public sealed class MasCommand
    {
        /// <summary>The name of the function, for example "HWID".</summary>
        public string Name { get; set; }

        /// <summary>The script that is run.</summary>
        public string ScriptFile { get; set; }

        /// <summary>The project the script comes from, shown to the user before a run.</summary>
        public string Source { get; set; }

        /// <summary>The arguments that are passed to the script.</summary>
        public string Arguments { get; set; }

        /// <summary>The answers that are given to the script when it asks.</summary>
        public string[] Answers { get; set; }

        /// <summary>
        /// True when the function asks which target should be used, for example the
        /// edition of Windows or the package of Office.
        /// </summary>
        public bool NeedsTarget { get; set; }

        /// <summary>The targets the user can pick from.</summary>
        public IList<MasTarget> Targets { get; set; }
    }

    /// <summary>
    /// One target of a function, for example one edition of Windows.
    /// </summary>
    public sealed class MasTarget
    {
        public string Label { get; set; }

        public string Description { get; set; }

        /// <summary>The answers that are given to the script for this target.</summary>
        public string[] Answers { get; set; }
    }
}
