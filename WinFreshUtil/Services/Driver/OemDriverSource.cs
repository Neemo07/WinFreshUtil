using System;
using System.Collections.Generic;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services.Driver
{
    /// <summary>
    /// The official tool of the manufacturer of the computer. It is used when
    /// Windows Update and the update catalog have no driver for the device.
    ///
    /// The tools come from the package manager of Windows, so that the same source
    /// that the user trusts for the programs is used here as well.
    /// </summary>
    public sealed class OemDriverSource : IDriverSource
    {
        public string Name
        {
            get { return "the official tool of the manufacturer of the computer"; }
        }

        /// <summary>
        /// Looks for the tools of the manufacturer. Runs on a background thread.
        /// </summary>
        public IList<DriverCandidate> Find(ProblemDevice device, Action<string> progress)
        {
            List<DriverCandidate> found = new List<DriverCandidate>();

            return found;
        }

        /// <summary>
        /// The tools that the manufacturer of the computer has.
        /// </summary>
        public static IList<OemTool> GetTools(string computerManufacturer)
        {
            List<OemTool> tools = new List<OemTool>();

            string lower = (computerManufacturer ?? string.Empty).ToLowerInvariant();

            if (lower.Contains("dell"))
            {
                tools.Add(new OemTool
                {
                    Name = "Dell Command Update",
                    WingetId = "Dell.CommandUpdate",
                    Page = "https://www.dell.com/support/kbdoc/en-us/000134838"
                });
            }

            if (lower.Contains("hp") || lower.Contains("hewlett"))
            {
                tools.Add(new OemTool
                {
                    Name = "HP Image Assistant",
                    WingetId = "HP.ImageAssistant",
                    Page = "https://support.hp.com/us-en/drivers/hp-image-assistant"
                });
            }

            if (lower.Contains("lenovo"))
            {
                tools.Add(new OemTool
                {
                    Name = "Lenovo Thin Installer",
                    WingetId = "Lenovo.ThinInstaller",
                    Page = "https://support.lenovo.com/us/en/solutions/ht500208"
                });
            }

            if (lower.Contains("intel"))
            {
                tools.Add(new OemTool
                {
                    Name = "Intel Driver & Support Assistant",
                    WingetId = "Intel.IntelDriverAndSupportAssistant",
                    Page = "https://www.intel.com/content/www/us/en/support/detect.html"
                });
            }

            return tools;
        }

        /// <summary>
        /// The drivers that stand for the tools of the manufacturer.
        /// </summary>
        public static IList<DriverCandidate> GetCandidates(string computerManufacturer)
        {
            List<DriverCandidate> found = new List<DriverCandidate>();

            foreach (OemTool tool in GetTools(computerManufacturer))
            {
                found.Add(new DriverCandidate
                {
                    Name = tool.Name,
                    Source = "the official tool of the manufacturer of the computer",
                    SourceUrl = tool.Page,
                    UpdateId = tool.WingetId,
                    Stage = DriverStage.OemTool
                });
            }

            return found;
        }

        /// <summary>
        /// Installs the tool of the manufacturer. Runs on a background thread with
        /// administrator rights.
        /// </summary>
        public bool Install(DriverCandidate candidate, DriverInstallContext context)
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.UpdateId))
            {
                context.Write("The tool of the manufacturer is not known.");
                return false;
            }

            context.Write(candidate.Name + " is installed with the package manager of Windows...");

            if (context.StopIfAsked("The installation was stopped."))
            {
                return false;
            }

            string winget = PackageManagerLocator.FindWinget();

            if (string.IsNullOrEmpty(winget))
            {
                context.Write("The package manager of Windows is not on this computer.");
                return false;
            }

            int exitCode;
            string output = DriverTools.RunProgram(winget,
                new[] { "install", "--id", candidate.UpdateId, "--exact", "--silent",
                        "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" },
                900000, out exitCode);

            context.Write(output);

            if (exitCode == 0 ||
                output.IndexOf("successfully", StringComparison.OrdinalIgnoreCase) >= 0 ||
                output.IndexOf("already installed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                context.Write(candidate.Name + " was installed. It can look for the drivers of the computer.");
                return true;
            }

            context.Write(candidate.Name + " could not be installed.");
            return false;
        }

    }
}
