using System.Collections.Generic;

namespace WinFreshUtil.Models
{
    /// <summary>
    /// One of the changes that WinUtil offers. The commands are run on the computer
    /// itself, in a hidden console, so that nothing is opened outside the application.
    /// </summary>
    public sealed class WinUtilTask
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        /// <summary>The commands that apply the change.</summary>
        public IList<string> Commands { get; set; }
    }

    /// <summary>
    /// The result of one change.
    /// </summary>
    public sealed class WinUtilResult
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public bool Done { get; set; }

        public string Message { get; set; }
    }
}
