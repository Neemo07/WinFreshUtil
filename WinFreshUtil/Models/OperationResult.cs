using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using WinFreshUtil.Services;

namespace WinFreshUtil.Models
{
    /// <summary>
    /// The result of a piece of work that is done in the background.
    /// </summary>
    public sealed class OperationResult
    {
        /// <summary>True when the work was done.</summary>
        public bool Success { get; set; }

        /// <summary>What happened, in words for the user.</summary>
        public string Message { get; set; }
    }
}
