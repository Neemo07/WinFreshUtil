using System;
using System.Collections.Generic;
using WinFreshUtil.Models;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Turns the functions that the Microsoft Activation Scripts offer into the commands
    /// that run them. The names are the ones that the script prints, so the list stays
    /// correct when the script is updated.
    /// </summary>
    public static class MasFunctionMapper
    {
        private const string AllInOne = "MAS_AIO.cmd";

        /// <summary>
        /// Returns the command for the function with the given name, or null when the
        /// function has no command of its own.
        /// </summary>
        public static MasCommand GetCommand(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return null;
            }

            string name = label.Trim();

            if (StartsWith(name, "HWID"))
            {
                return new MasCommand
                {
                    Name = name,
                    ScriptFile = AllInOne,
                    Source = ActivationService.RepositoryUrl,
                    Arguments = "/HWID",
                    Answers = new string[0]
                };
            }

            if (StartsWith(name, "Ohook"))
            {
                return new MasCommand
                {
                    Name = name,
                    ScriptFile = AllInOne,
                    Source = ActivationService.RepositoryUrl,
                    Arguments = "/Ohook",
                    Answers = new string[0]
                };
            }

            if (StartsWith(name, "TSforge"))
            {
                return new MasCommand
                {
                    Name = name,
                    ScriptFile = AllInOne,
                    Source = ActivationService.RepositoryUrl,
                    Arguments = "/Z-WindowsESUOffice",
                    Answers = new string[0]
                };
            }

            if (StartsWith(name, "Online KMS"))
            {
                return new MasCommand
                {
                    Name = name,
                    ScriptFile = AllInOne,
                    Source = ActivationService.RepositoryUrl,
                    Arguments = "/K-WindowsOffice",
                    Answers = new string[0]
                };
            }

            if (StartsWith(name, "Check Activation Status"))
            {
                return new MasCommand
                {
                    Name = name,
                    ScriptFile = "Check_Activation_Status.cmd",
                    Source = ActivationService.RepositoryUrl,
                    Arguments = string.Empty,
                    Answers = new string[0]
                };
            }

            if (StartsWith(name, "Change Windows Edition"))
            {
                return new MasCommand
                {
                    Name = name,
                    ScriptFile = "Change_Windows_Edition.cmd",
                    Source = ActivationService.RepositoryUrl,
                    Arguments = string.Empty,
                    Answers = new string[0],
                    NeedsTarget = true
                };
            }

            if (StartsWith(name, "Change Office Edition"))
            {
                return new MasCommand
                {
                    Name = name,
                    ScriptFile = "Change_Office_Edition.cmd",
                    Source = ActivationService.RepositoryUrl,
                    Arguments = string.Empty,
                    Answers = new string[0],
                    NeedsTarget = true
                };
            }

            return null;
        }

        /// <summary>
        /// The targets of the function that installs a different Office edition.
        /// </summary>
        public static IList<MasTarget> GetOfficeTargets()
        {
            List<MasTarget> targets = new List<MasTarget>();

            targets.Add(new MasTarget
            {
                Label = "Microsoft 365 / Office LTSC, retail",
                Description = "Installs the retail suite of Microsoft 365 or Office LTSC.",
                Answers = new[] { "1", "1" }
            });

            targets.Add(new MasTarget
            {
                Label = "Microsoft 365 / Office LTSC, volume licence",
                Description = "Installs the volume suite of Microsoft 365 or Office LTSC.",
                Answers = new[] { "1", "2" }
            });

            targets.Add(new MasTarget
            {
                Label = "Single applications, retail",
                Description = "Installs single Office applications such as Word or Excel with a retail licence.",
                Answers = new[] { "1", "3" }
            });

            targets.Add(new MasTarget
            {
                Label = "Single applications, volume licence",
                Description = "Installs single Office applications with a volume licence.",
                Answers = new[] { "1", "4" }
            });

            return targets;
        }

        private static bool StartsWith(string value, string prefix)
        {
            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
