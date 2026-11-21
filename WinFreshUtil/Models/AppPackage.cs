using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace WinFreshUtil.Models
{
    /// <summary>
    /// One program that can be installed with the package manager of Windows.
    /// </summary>
    public sealed class AppPackage
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string Version { get; set; }
        public string Source { get; set; }
    }
}
