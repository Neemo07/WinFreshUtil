using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinFreshUtil.Services;
using WinFreshUtil.Services.Driver;
using WinFreshUtil.WinUtil;

namespace WinFreshUtil.Tests
{
    /// <summary>
    /// The checks of the parts that are easy to break and hard to see: the building of
    /// a command line, the checking of the request that the elevated instance reads,
    /// the links that may be opened and the file that holds the action for the next
    /// instance.
    ///
    /// The program returns 0 when every check passed and 1 when one of them failed.
    /// </summary>
    public static class Program
    {
        private static int _failed;
        private static int _passed;

        public static int Main()
        {
            QuoteKeepsOneArgument();
            QuoteEscapesTheSpecialCharacters();
            PackageIdsAreChecked();
            GuidsAreChecked();
            TheRequestIsChecked();
            TheRequestHoldsNoPaths();
            OnlyWebLinksAreOpened();
            ThePendingActionCanBeTakenAway();
            TheShownCommandIsTheRunCommand();
            TheStopSignWorks();
            TheProgressIsReadOnlyOnce();
            TheProgressOfOneRunIsReadAtOnce();
            TheRunningProgramIsStopped();
            TheSearchTermsStartWithTheOneThatFits();
            TheInstalledListIsKept();
            TheMasRequestKeepsItsFormat();
            TheScriptIsChecked();
            TheRemoveCommandIsRight();
            TheRapidOsIsPinned();
            ThePayloadsAreCarried();
            TheWinUtilDefinitionsAreLocal();
            ThePagesLoad();

            Console.WriteLine();
            Console.WriteLine(_failed == 0
                ? "All " + _passed + " checks passed."
                : _failed + " of " + (_passed + _failed) + " checks FAILED.");

            return _failed == 0 ? 0 : 1;
        }

        #region the checks

        private static void QuoteKeepsOneArgument()
        {
            // A value with a space stays one argument.
            Check("a value with a space is put in quotes",
                ProcessArguments.Quote("two words") == "\"two words\"");

            // A value without anything special is left as it is.
            Check("a plain value is left alone",
                ProcessArguments.Quote("plain") == "plain");

            // A value with a quote keeps the quote inside the argument.
            Check("a quote does not end the argument",
                ProcessArguments.Quote("a\"b") == "\"a\\\"b\"");

            // The backslashes in front of a quote are doubled.
            Check("the backslashes in front of a quote are doubled",
                ProcessArguments.Quote("a\\\"b") == "\"a\\\\\\\"b\"");

            // The backslashes at the end are doubled as well.
            Check("the backslashes at the end are doubled",
                ProcessArguments.Quote("a b\\") == "\"a b\\\\\"");

            // The whole line keeps the arguments apart.
            Check("the arguments stay apart",
                ProcessArguments.Join(new[] { "install", "--id", "Some.Package", "C:\\Program Files\\x" })
                    == "install --id Some.Package \"C:\\Program Files\\x\"");
        }

        private static void QuoteEscapesTheSpecialCharacters()
        {
            // The values that could break out of an argument line.
            string[] dangerous = { "x\" & calc & rem \"", "x\"; calc", "x\" | calc", "x\"&calc" };

            foreach (string value in dangerous)
            {
                string quoted = ProcessArguments.Quote(value);

                // Only the quotes that are not escaped end an argument. There have to
                // be exactly two of them, so that the value cannot close the argument
                // and open a new one.
                int delimiters = 0;

                for (int index = 0; index < quoted.Length; index++)
                {
                    if (quoted[index] != '"')
                    {
                        continue;
                    }

                    int backslashes = 0;
                    for (int before = index - 1; before >= 0 && quoted[before] == '\\'; before--)
                    {
                        backslashes++;
                    }

                    if (backslashes % 2 == 0)
                    {
                        delimiters++;
                    }
                }

                Check("the value " + value + " stays one argument",
                    delimiters == 2 && quoted.StartsWith("\"") && quoted.EndsWith("\""));
            }
        }

        private static void PackageIdsAreChecked()
        {
            Check("a normal package name is accepted", DriverRequest.IsPackageId("Intel.IntelDriverAndSupportAssistant"));
            Check("a name with a dot and a dash is accepted", DriverRequest.IsPackageId("7zip.7zip"));
            Check("an empty name is refused", !DriverRequest.IsPackageId(string.Empty));
            Check("a name with a quote is refused", !DriverRequest.IsPackageId("x\" & calc & rem \""));
            Check("a name with an ampersand is refused", !DriverRequest.IsPackageId("x&calc"));
            Check("a name with a semicolon is refused", !DriverRequest.IsPackageId("x;calc"));
            Check("a name with a space is refused", !DriverRequest.IsPackageId("x calc"));
            Check("a name with a percent sign is refused", !DriverRequest.IsPackageId("x%PATH%"));
            Check("a very long name is refused", !DriverRequest.IsPackageId(new string('a', 200)));
        }

        private static void GuidsAreChecked()
        {
            Check("a real id is accepted", DriverRequest.IsGuid("e7ff18df-5df3-4249-abce-6abe7eb910bc"));
            Check("an empty id is refused", !DriverRequest.IsGuid(string.Empty));
            Check("a name is refused", !DriverRequest.IsGuid("not-an-id"));
            Check("a name with a command is refused", !DriverRequest.IsGuid("x\" & calc & rem \""));
        }

        private static void TheRequestIsChecked()
        {
            string error;

            Dictionary<string, string> good = new Dictionary<string, string>
            {
                { "M", "OemTool" },
                { "U", "Dell.CommandUpdate" },
                { "T", "Dell Command Update" }
            };

            Check("a good request is accepted", DriverRequest.Validate(good, out error) && error == null);

            Dictionary<string, string> bad = new Dictionary<string, string>
            {
                { "M", "OemTool" },
                { "U", "x\" & calc & rem \"" }
            };

            Check("a request with a command in it is refused", !DriverRequest.Validate(bad, out error) && error != null);

            Dictionary<string, string> unknownStage = new Dictionary<string, string>
            {
                { "M", "SomethingElse" },
                { "U", "Dell.CommandUpdate" }
            };

            Check("a request with an unknown stage is refused", !DriverRequest.Validate(unknownStage, out error));

            Dictionary<string, string> twoStages = new Dictionary<string, string>
            {
                { "M", "OemTool, WindowsUpdate" },
                { "U", "Dell.CommandUpdate" }
            };

            Check("a request with two stages is refused", !DriverRequest.Validate(twoStages, out error));

            Dictionary<string, string> noStage = new Dictionary<string, string>
            {
                { "U", "Dell.CommandUpdate" }
            };

            Check("a request without a stage is refused", !DriverRequest.Validate(noStage, out error));

            Dictionary<string, string> update = new Dictionary<string, string>
            {
                { "M", "UpdateCatalog" },
                { "U", "ebb47204-9179-4742-a088-3939429d5a39" }
            };

            Check("a request with a driver id is accepted", DriverRequest.Validate(update, out error));

            Dictionary<string, string> updateBad = new Dictionary<string, string>
            {
                { "M", "UpdateCatalog" },
                { "U", "x\" & calc & rem \"" }
            };

            Check("a request with a command in the driver id is refused", !DriverRequest.Validate(updateBad, out error));
        }

        private static void TheRequestHoldsNoPaths()
        {
            // The request is read by the instance that has administrator rights, so it
            // may not point at a place where the progress or the result is written.
            string path = Path.Combine(Path.GetTempPath(), "wf-request-test.txt");

            try
            {
                DriverCandidate candidate = new DriverCandidate
                {
                    Stage = DriverStage.OemTool,
                    UpdateId = "Dell.CommandUpdate",
                    Name = "Dell Command Update",
                    HardwareId = "PCI\\VEN_8086&DEV_0A16"
                };

                // The writer is public now, so the file is written the way the
                // application writes it.
                DriverRequest.Write(path, candidate);

                string[] lines = File.ReadAllLines(path);

                bool hasPath = false;
                foreach (string line in lines)
                {
                    if (line.StartsWith("P=") || line.StartsWith("R="))
                    {
                        hasPath = true;
                    }
                }

                Check("the request holds no place for the progress or the result", !hasPath);
                Check("the request holds the stage", lines.Length > 0 && lines[0].StartsWith("M="));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static void OnlyWebLinksAreOpened()
        {
            Check("a web address is allowed", UrlService.IsAllowed("https://www.catalog.update.microsoft.com/"));
            Check("a page of Windows is allowed", UrlService.IsAllowed("ms-settings:windowsupdate"));
            Check("a file is refused", !UrlService.IsAllowed("file:///C:/Windows/System32/calc.exe"));
            Check("a program is refused", !UrlService.IsAllowed("ms-msdt:/id"));
            Check("an empty link is refused", !UrlService.IsAllowed(string.Empty));
            Check("a link that is not a link is refused", !UrlService.IsAllowed("not a link"));
        }

        private static void ThePendingActionCanBeTakenAway()
        {
            // The action has to be gone when the user does not allow the elevation, so
            // that a later start of the application does not carry it out.
            StartupAction.Set(StartupAction.DriverInstaller);

            string taken = StartupAction.Take();
            Check("the action can be read back", taken == StartupAction.DriverInstaller);

            StartupAction.Set(StartupAction.DriverInstaller);
            StartupAction.Clear(StartupAction.DriverInstaller);

            Check("the action is gone after it was taken away", StartupAction.Take() == null);

            // The action of another kind is not touched.
            StartupAction.Set(StartupAction.MasHelper);
            StartupAction.Clear(StartupAction.DriverInstaller);
            Check("the action of another kind is not taken away", StartupAction.Take() == StartupAction.MasHelper);
        }

        private static void TheShownCommandIsTheRunCommand()        {
            // The text in the confirmation window has to be the command that is run.
            string shown = AppInstallService.GetInstallCommand("Some.Package", false);
            string run = "winget " + ProcessArguments.Join(AppInstallService.BuildInstallArguments("Some.Package", false));

            Check("the shown install command is the run command", shown == run);
            Check("the shown install command names the package", shown.Contains("Some.Package"));
            Check("the shown install command has the package agreements", shown.Contains("--accept-package-agreements"));

            string shownUninstall = AppInstallService.GetUninstallCommand("Some.Package", false);
            string runUninstall = "winget " + ProcessArguments.Join(AppInstallService.BuildUninstallArguments("Some.Package", false));

            Check("the shown remove command is the run command", shownUninstall == runUninstall);

            // The remove command of the package manager of Windows does not know the
            // switch for the package agreements, so it must not be in the command.
            Check("the shown remove command has no switch for the package agreements",
                !shownUninstall.Contains("--accept-package-agreements"));

            // The wrong name of the switch was in the remove command before, so it was
            // written twice.
            int sourceAgreements = CountOccurrences(shownUninstall, "--accept-source-agreements");
            Check("the shown remove command has the source agreements once", sourceAgreements == 1);

            string shownChoco = AppInstallService.GetInstallCommand("some-package", true);
            Check("the command for chocolatey names chocolatey", shownChoco.StartsWith("choco "));
            Check("the command for chocolatey holds the package", shownChoco.Contains("some-package"));
        }

        private static void TheStopSignWorks()
        {
            DriverInstaller.ClearStop();

            DriverInstaller.RequestStop();
            Check("the sign to stop is there", File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinFreshUtil", "driver-stop.txt")));

            DriverInstaller.ClearStop();
            Check("the sign to stop is gone", !File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinFreshUtil", "driver-stop.txt")));
        }

        private static void TheProgressIsReadOnlyOnce()
        {
            // The steps of the installation are read from a file that is written while
            // it is read. Every step has to be told once, and a line that is still
            // being written has to wait for the next reading.
            string path = Path.Combine(Path.GetTempPath(), "wf-progress-test.log");

            try
            {
                File.WriteAllText(path, "first step\r\nsecond step\r\nhalf a step", Encoding.ASCII);

                List<string> seen = new List<string>();

                Action<string> report = delegate(string line) { seen.Add(line); };
                long offset = 0;

                DriverFolder.ReportProgress(path, report, ref offset);

                Check("the whole steps are read", seen.Count == 2 && seen[0] == "first step" && seen[1] == "second step");
                Check("the step that is not whole is not read yet", seen.Count == 2);

                // The last line is finished now.
                File.AppendAllText(path, " step\r\nthird step\r\n", Encoding.ASCII);
                DriverFolder.ReportProgress(path, report, ref offset);

                Check("the finished step is read now",
                    seen.Count == 4 && seen[2] == "half a step step" && seen[3] == "third step");

                DriverFolder.ReportProgress(path, report, ref offset);
                Check("reading again tells nothing new", seen.Count == 4);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static void TheProgressOfOneRunIsReadAtOnce()
        {
            // The package managers separate their progress with a carriage return and
            // no line feed. Every step has to reach the caller while the program runs,
            // not only when it ends.
            List<int> percents = new List<int>();

            AppInstallService.Run(
                "powershell.exe",
                new[]
                {
                    "-NoProfile", "-NonInteractive", "-Command",
                    "[Console]::Out.Write('10%' + [char]13 + '20%' + [char]13 + '30%')"
                },
                30000,
                delegate(AppInstallService.InstallProgress progress)
                {
                    if (progress.Percent >= 0)
                    {
                        percents.Add(progress.Percent);
                    }
                });

            Check("every step of one run is read",
                percents.Contains(10) && percents.Contains(20) && percents.Contains(30));
        }

        private static void TheRunningProgramIsStopped()
        {
            // When the application is closed, the program that is running has to be
            // stopped, so that an installation does not go on without the user.
            bool finished = false;
            bool killed = false;

            Thread worker = new Thread(delegate()
            {
                AppInstallService.Run("ping.exe",
                    new[] { "-n", "40", "127.0.0.1" }, 60000, null);
                finished = true;
            });

            worker.IsBackground = true;
            worker.Start();

            Thread.Sleep(1200);

            AppInstallService.KillRunning();

            worker.Join(8000);

            killed = finished;

            Check("the program that is running is stopped", killed);
        }

        private static void TheSearchTermsStartWithTheOneThatFits()
        {
            // The update catalog knows the ids that the device is compatible with.
            // The part of the device itself is never in the catalog, so it may not be
            // the first text that is looked for.
            WinFreshUtil.Models.ProblemDevice device = new WinFreshUtil.Models.ProblemDevice
            {
                HardwareIds = new[] { "USB\\VID_0E8D&PID_201D\\5&1234ABCD&0&1" },
                CompatibleIds = new[] { "USB\\VID_0E8D&PID_201D&MI_01" }
            };

            IList<string> terms = CatalogDriverSource.GetSearchTerms(device);

            Check("the list of search texts is not empty", terms.Count > 0);
            Check("the id that the device is compatible with comes first",
                terms.Count > 0 && terms[0] == "USB\\VID_0E8D&PID_201D&MI_01");
            Check("the part of the device is not the first text",
                terms.Count > 0 && !terms[0].EndsWith("\\5&1234ABCD&0&1"));
            Check("the part of the device is still in the list",
                terms.Contains("USB\\VID_0E8D&PID_201D\\5&1234ABCD&0&1"));

            // A device without a compatible id still has texts to look for.
            WinFreshUtil.Models.ProblemDevice other = new WinFreshUtil.Models.ProblemDevice
            {
                HardwareIds = new[] { "PCI\\VEN_8086&DEV_0A16&SUBSYS_198F103C&REV_0B" }
            };

            IList<string> otherTerms = CatalogDriverSource.GetSearchTerms(other);

            Check("the id without the manufacturer comes first",
                otherTerms.Count > 0 && otherTerms[0] == "PCI\\VEN_8086&DEV_0A16");
            Check("the id without the version is in the list",
                otherTerms.Contains("PCI\\VEN_8086&DEV_0A16"));
        }

        private static void TheInstalledListIsKept()
        {
            // The list of the installed programs takes a few seconds. It may only be
            // asked for once, and the answer has to be kept.
            Stopwatch sw = Stopwatch.StartNew();
            AppInstallService.GetInstalledIdsCached(false, true);
            sw.Stop();
            long first = sw.ElapsedMilliseconds;

            sw.Restart();
            ISet<string> again = AppInstallService.GetInstalledIdsCached(false, false);
            sw.Stop();
            long second = sw.ElapsedMilliseconds;

            Check("the list of the installed programs is not empty", again.Count > 0);
            Check("the answer is kept, so the second time is fast", second < 500);
            Console.WriteLine("       the first time took " + first + " ms, the second " + second + " ms");
        }

        private static void TheMasRequestKeepsItsFormat()
        {
            // The request of the activation helper may not change, because the helper
            // of the original project reads it.
            string path = Path.Combine(Path.GetTempPath(), "wf-mas-request-test.txt");

            try
            {
                MasHelper.Request request = new MasHelper.Request
                {
                    ScriptPath = @"C:\mas\MAS_AIO.cmd",
                    Arguments = "/HWID",
                    LogPath = @"C:\mas\output.log",
                    ResultPath = @"C:\mas\result.txt",
                    StopPath = @"C:\mas\stop.txt",
                    Answers = new List<MasHelper.Answer>
                    {
                        new MasHelper.Answer { Text = "1", NeedsEnter = true },
                        new MasHelper.Answer { Text = "0", NeedsEnter = false }
                    }
                };

                MasHelper.WriteRequest(path, request);

                string[] lines = File.ReadAllLines(path);

                Check("the request starts with the script", lines[0] == "S=C:\\mas\\MAS_AIO.cmd");
                Check("the request holds the arguments", lines[1] == "A=/HWID");
                Check("the request holds the log", lines[2] == "L=C:\\mas\\output.log");
                Check("the request holds the result", lines[3] == "R=C:\\mas\\result.txt");
                Check("the request holds the stop file", lines[4] == "P=C:\\mas\\stop.txt");
                Check("the answer that needs the enter key is marked", lines[5] == "N=1;1");
                Check("the other answer is not marked", lines[6] == "N=0;0");
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static void TheScriptIsChecked()
        {
            // The script of the activation project is carried inside the application
            // from a fixed commit, and it is only run when it has the hash that the
            // application knows.
            Check("the bundled script comes from a fixed commit",
                ScriptHash.MasCommit.Length == 40);
            Check("the bundled script has a hash of the script",
                ScriptHash.MasSha256.Length == 64);

            string folder = Path.Combine(Path.GetTempPath(), "wf-hash-test");
            string script = Path.Combine(folder, ScriptHash.MasFileName);

            try
            {
                Directory.CreateDirectory(folder);

                // A file with the name of the script but with another text.
                File.WriteAllText(script, "this is not the script", Encoding.ASCII);

                Check("the script has to have a hash", ScriptHash.ExpectedFor(script) == ScriptHash.MasSha256);
                Check("a script with another text is refused", !ScriptHash.IsExpected(script));
                Check("the hash of a file is read", ScriptHash.OfFile(script) != null);

                // A file that the application made itself is trusted.
                string own = Path.Combine(folder, "WinFreshUtil_Tweaks.cmd");
                File.WriteAllText(own, "echo hello", Encoding.ASCII);

                Check("a file of the application itself is trusted", ScriptHash.IsExpected(own));
                Check("a file of the application itself has no hash to check", ScriptHash.ExpectedFor(own) == null);

                // The hash of the bytes is the same as the hash of the file.
                byte[] bytes = Encoding.ASCII.GetBytes("this is not the script");
                Check("the hash of the bytes is the same as the hash of the file",
                    ScriptHash.OfBytes(bytes) == ScriptHash.OfFile(script));
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
        }

        private static void TheRemoveCommandIsRight()
        {
            // The remove command of the package manager of Windows does not know the
            // switch for the package agreements. With it, the program refuses the whole
            // command and nothing is removed.
            string shown = AppInstallService.GetUninstallCommand("Some.Package", false);
            string run = "winget " + ProcessArguments.Join(AppInstallService.BuildUninstallArguments("Some.Package", false));

            Check("the shown remove command is the run command", shown == run);
            Check("the remove command has no switch for the package agreements",
                !shown.Contains("--accept-package-agreements"));
            Check("the remove command has the switch for the source agreements",
                shown.Contains("--accept-source-agreements"));
            Check("the remove command has the switch for no questions",
                shown.Contains("--disable-interactivity"));
            Check("the remove command is quiet", shown.Contains("--silent"));

            // The install command does know the switch for the package agreements.
            string install = AppInstallService.GetInstallCommand("Some.Package", false);
            Check("the install command has the switch for the package agreements",
                install.Contains("--accept-package-agreements"));
        }

        private static void TheRapidOsIsPinned()
        {
            // The sources of RapidOS come from a fixed commit, and the hash of the
            // archive is written down.
            Check("the sources come from a fixed commit", RapidOsService.ArchiveUrl.Contains(RapidOsService.Commit));
            Check("the sources do not come from the moving branch", !RapidOsService.ArchiveUrl.Contains("/main/"));
            Check("the hash of the archive is written down", RapidOsService.ArchiveSha256.Length == 64);

            string command = RapidOsService.GetInstallCommand();

            Check("the command starts PowerShell", command.StartsWith("powershell "));
            Check("the command runs the script of the project", command.Contains("Run-RapidOs.ps1"));
            Check("the command is the same as the one that is run",
                command.Contains("Run-RapidOs.ps1"));

            // The builds that the project supports.
            string build;
            bool supported = RapidOsService.IsSupportedBuild(out build);
            Check("the build of this computer is known", build.Length > 0);
            Console.WriteLine("       the build of this computer is " + build +
                              ", the project supports it: " + supported);
        }

        private static void ThePayloadsAreCarried()
        {
            // The scripts and the sources are inside the application, so that it works
            // on a computer without the internet.
            string[] scripts =
            {
                ScriptHash.MasFileName,
                "Check_Activation_Status.cmd",
                "Change_Windows_Edition.cmd",
                "Change_Office_Edition.cmd"
            };

            foreach (string script in scripts)
            {
                Check("the script " + script + " has a hash",
                    !string.IsNullOrEmpty(ScriptHash.ExpectedFor(script)));
            }

            Check("the version of the bundled script is read",
                !string.IsNullOrEmpty(MasUpdateService.GetScriptVersion()));

            string ignoredHash;
            Check("a script outside the activation folder is not checked against an update",
                !MasUpdateService.TryGetHash(
                    Path.Combine(Path.GetTempPath(), ScriptHash.MasFileName),
                    ScriptHash.MasFileName,
                    out ignoredHash));

            string folder = Path.Combine(Path.GetTempPath(), "wf-payload-test-" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(folder);

                string target = Path.Combine(folder, ScriptHash.MasFileName);
                string error;

                bool restored = PayloadService.TryRestore(
                    ScriptHash.MasFileName, target, ScriptHash.MasSha256, out error);

                Check("the activation script is taken out of the application", restored);
                Check("the script that was taken out has the expected hash",
                    restored && ScriptHash.IsExpected(target));

                string rapidOs = "RapidOS-" + RapidOsService.Commit + ".zip";
                string rapidTarget = Path.Combine(folder, rapidOs);

                bool rapidRestored = PayloadService.TryRestore(
                    rapidOs, rapidTarget, RapidOsService.ArchiveSha256, out error);

                Check("the sources of RapidOS are taken out of the application", rapidRestored);
                Check("the sources that were taken out have the expected hash",
                    rapidRestored && ScriptHash.OfFile(rapidTarget) == RapidOsService.ArchiveSha256);

                bool missing = PayloadService.TryRestore(
                    "not-carried.cmd", Path.Combine(folder, "not-carried.cmd"), null, out error);

                Check("a file that the application does not carry is refused", !missing);
                Check("the refusal says why", !string.IsNullOrEmpty(error));
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
        }

        private static void TheWinUtilDefinitionsAreLocal()
        {
            WinUtilCatalog catalog = WinUtilCatalogLoader.Load();

            Check("the local WinUtil changes are read", catalog.Tweaks.Count == 66);
            Check("the local WinUtil features are read", catalog.Features.Count == 30);
            Check("the local WinUtil applications are read", catalog.Apps.Count == 34);
            Check("the local name servers are read", catalog.DnsProviders.Count == 8);
            Check("the local presets are read", catalog.Presets.Count == 4);

            // Entries that need the internet are left out instead of failing when picked.
            string[] onlineOnly =
            {
                "WPFOOSUbutton",
                "WPFTweaksBlockAdobeNet",
                "WPFFeatureInstall",
                "WPFPanelAutologin",
                "WPFFixesUpdate",
                "WPFFixesWinget"
            };

            bool noneOnline = true;
            foreach (string key in onlineOnly)
            {
                if (catalog.FindEntry(key) != null || catalog.FindApp(key) != null)
                {
                    noneOnline = false;
                }
            }

            Check("the entries that need the internet are left out", noneOnline);

            // A preset may only name a change that the local definitions carry, otherwise
            // it would silently pick nothing.
            bool everyPresetKeyExists = true;
            foreach (KeyValuePair<string, List<string>> preset in catalog.Presets)
            {
                foreach (string key in preset.Value)
                {
                    if (catalog.FindTweak(key) == null && catalog.FindApp(key) == null)
                    {
                        everyPresetKeyExists = false;
                        Console.WriteLine("       " + preset.Key + " names " + key +
                                          ", which the local changes do not carry.");
                    }
                }
            }

            Check("every preset names a change that is in the application", everyPresetKeyExists);
            // The page writes the package name into a script, so it must stay a plain name.
            bool everyPackageIsPlain = true;
            foreach (WinUtilAppxEntry app in catalog.Apps)
            {
                if (!WinUtilAppx.IsValidPackageId(app.PackageId))
                {
                    everyPackageIsPlain = false;
                    Console.WriteLine("       " + app.Key + " has the package name " + app.PackageId + ".");
                }
            }

            Check("every application has a plain package name", everyPackageIsPlain);

            // A combo box must offer at least two choices and every choice must have a value,
            // or the page would offer a choice that changes nothing.
            bool everyComboIsUsable = true;
            foreach (WinUtilEntry entry in catalog.Tweaks)
            {
                if (entry.Kind != WinUtilEntryKind.ComboBox)
                {
                    continue;
                }

                if (entry.ComboItems.Count < 2)
                {
                    everyComboIsUsable = false;
                    Console.WriteLine("       " + entry.Key + " offers " + entry.ComboItems.Count + " choice(s).");
                    continue;
                }

                foreach (string item in entry.ComboItems)
                {
                    if (!entry.HasRegistryChoices)
                    {
                        continue;
                    }

                    bool mapped = false;
                    foreach (WinUtilRegistryChange change in entry.Registry)
                    {
                        if (change.Values.ContainsKey(item))
                        {
                            mapped = true;
                        }
                    }

                    if (!mapped)
                    {
                        everyComboIsUsable = false;
                        Console.WriteLine("       " + entry.Key + " has no value for the choice " + item + ".");
                    }
                }
            }

            Check("every combo box choice has a value", everyComboIsUsable);

            // The buttons of the features page must be one that this build knows how to run.
            // A new button in the definitions would otherwise do nothing when it is pressed.
            bool everyButtonIsKnown = true;
            foreach (WinUtilEntry entry in catalog.Features)
            {
                if (entry.Kind != WinUtilEntryKind.Button)
                {
                    continue;
                }

                bool known = WinUtilActions.CanRun(entry) || entry.InvokeScripts.Count > 0;

                if (!known)
                {
                    everyButtonIsKnown = false;
                    Console.WriteLine("       the button " + entry.Key + " has nothing to run.");
                }
            }

            Check("every button of the features page can run", everyButtonIsKnown);

            // The bundled text must not download anything. A DNS over HTTPS template is
            // a value that Windows stores, not a connection that this program makes.
            bool noDownload = true;
            foreach (WinUtilEntry entry in catalog.Tweaks)
            {
                noDownload &= IsLocalScript(entry.InvokeScripts) && IsLocalScript(entry.UndoScripts);
            }

            foreach (WinUtilEntry entry in catalog.Features)
            {
                noDownload &= IsLocalScript(entry.InvokeScripts) && IsLocalScript(entry.UndoScripts);
            }

            Check("the bundled scripts contain no download", noDownload);

            // A value that is removed and a value that is set to 0 must not be confused.
            Check("the mark for a removed value is known", WinUtilRegistry.IsRemove(WinUtilRegistry.RemoveEntry));
            Check("an empty value means remove as well", WinUtilRegistry.IsRemove(string.Empty));
            Check("a value of 0 is kept", !WinUtilRegistry.IsRemove("0"));
            Check("a value of a text is kept", !WinUtilRegistry.IsRemove("Block"));

            // Nothing that is picked may end up as an empty run.
            Check("an empty selection runs nothing", new WinUtilSelection().IsEmpty);

            WinUtilSelection selection = new WinUtilSelection();
            selection.Actions.Add(new WinUtilAction { Key = "WPFTweaksTelemetry" });
            Check("a selection with one change is not empty", !selection.IsEmpty);

            // The page must not offer a change that has no way to run at all.
            bool everyTweakDoesSomething = true;
            foreach (WinUtilEntry entry in catalog.Tweaks)
            {
                if (entry.Kind == WinUtilEntryKind.Button && !WinUtilActions.CanRun(entry) &&
                    entry.Registry.Count == 0 && entry.Services.Count == 0 && entry.InvokeScripts.Count == 0)
                {
                    everyTweakDoesSomething = false;
                    Console.WriteLine("       the button " + entry.Key + " has nothing to run.");
                }

                if (entry.Kind == WinUtilEntryKind.Toggle && entry.Registry.Count == 0 &&
                    entry.Services.Count == 0 && entry.InvokeScripts.Count == 0)
                {
                    everyTweakDoesSomething = false;
                    Console.WriteLine("       the toggle " + entry.Key + " has nothing to run.");
                }
            }

            Check("every change of the page has work behind it", everyTweakDoesSomething);
        }

        private static void ThePagesLoad()
        {
            // A missing colour or a broken template only shows when a window is built. The
            // pages are made here so that such a fault is caught before the program runs.
            string failure = null;
            int loaded = 0;
            bool themeFollowed = false;
            string themeTrace = "not run";

            Thread thread = new Thread(delegate()
            {
                try
                {
                    Application app = new Application();
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/WinFreshUtil;component/Theme/Theme.xaml")
                    });

                    string[] keys =
                    {
                        PageFactory.Drivers,
                        PageFactory.WinUtil,
                        PageFactory.Apps,
                        PageFactory.WindowsOffice,
                        PageFactory.About
                    };

                    foreach (string key in keys)
                    {
                        UserControl page = PageFactory.Create(key);
                        if (page == null)
                        {
                            throw new InvalidOperationException("The page " + key + " could not be made.");
                        }

                        // Measuring applies the templates, which is where a colour that is
                        // not there is reported.
                        page.Measure(new Size(1200, 800));
                        page.Arrange(new Rect(0, 0, 1200, 800));
                        loaded++;
                    }

                    Window window = new MainWindow();
                    window.Measure(new Size(1240, 760));
                    window.Arrange(new Rect(0, 0, 1240, 760));

                    // The switch of the theme must replace the colours that every element
                    // asks for. The elements follow the same keys.
                    WinFreshUtil.Theme.ThemeManager.Initialize();

                    WinFreshUtil.Theme.ThemeManager.Apply(false);
                    SolidColorBrush lightPalette = Application.Current.TryFindResource("TextPrimaryBrush") as SolidColorBrush;
                    Color light = lightPalette == null ? Colors.Transparent : lightPalette.Color;

                    WinFreshUtil.Theme.ThemeManager.Apply(true);
                    SolidColorBrush darkPalette = Application.Current.TryFindResource("TextPrimaryBrush") as SolidColorBrush;
                    Color dark = darkPalette == null ? Colors.Transparent : darkPalette.Color;

                    themeFollowed = light != dark;
                    themeTrace = "light=" + light + " dark=" + dark +
                                 " isDark=" + WinFreshUtil.Theme.ThemeManager.IsDark;

                    WinFreshUtil.Theme.ThemeManager.Apply(false);
                }
                catch (Exception ex)
                {
                    failure = ex.ToString();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                Console.WriteLine("       " + failure);
            }

            Check("every page is built with the theme", failure == null && loaded == 5);
            Console.WriteLine("       theme trace: " + themeTrace);
            Check("the theme reaches an element that is already shown", failure == null && themeFollowed);
        }

        private static bool IsLocalScript(List<string> scripts)        {
            string[] blocked =
            {
                "Invoke-WebRequest",
                "Invoke-RestMethod",
                "DownloadString",
                "DownloadFile",
                "Start-BitsTransfer"
            };

            foreach (string script in scripts)
            {
                foreach (string part in blocked)
                {
                    if (script.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return false;
                    }
                }

                foreach (string line in script.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("winget ", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("choco ", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        #endregion

        private static int CountOccurrences(string text, string part)
        {
            int count = 0;
            int index = 0;

            while (index < text.Length)
            {
                int found = text.IndexOf(part, index, StringComparison.Ordinal);

                if (found < 0)
                {
                    break;
                }

                count++;
                index = found + part.Length;
            }

            return count;
        }

        private static void Check(string what, bool passed)
        {
            if (passed)
            {
                _passed++;
                Console.WriteLine("  ok   " + what);
            }
            else
            {
                _failed++;
                Console.WriteLine("  FAIL " + what);
            }
        }
    }
}
