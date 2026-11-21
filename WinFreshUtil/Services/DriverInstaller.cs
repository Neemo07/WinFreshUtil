using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using WinFreshUtil.Models;
using WinFreshUtil.Services.Driver;

namespace WinFreshUtil.Services
{
    /// <summary>
    /// Looks for the driver of a device and installs it.
    ///
    /// The driver is looked for in this order:
    /// 1. Windows Update, through the update agent of Windows.
    /// 2. The Microsoft Update Catalog, with the ids of the device.
    /// 3. The official tool of the manufacturer of the computer.
    /// 4. The support page of the manufacturer, which the user has to open by hand.
    ///
    /// Installing a driver needs administrator rights, so the installation is done by a
    /// second, elevated instance of this application. The instance writes every step
    /// into a file, which the application reads while it waits.
    /// </summary>
    public static class DriverInstaller
    {

        private static readonly IDriverSource WindowsUpdate = new WuaDriverSource();
        private static readonly IDriverSource UpdateCatalog = new CatalogDriverSource();
        private static readonly IDriverSource OemTool = new OemDriverSource();

        /// <summary>
        /// The file the elevated instance reads its work from.
        /// </summary>
        public static string GetRequestPath()
        {
            return DriverFolder.GetRequestPath();
        }

        /// <summary>
        /// Asks the elevated instance to stop.
        /// </summary>
        public static void RequestStop()
        {
            DriverFolder.RequestStop();
        }

        /// <summary>
        /// Takes the sign to stop away, so that an earlier run does not stop a new one.
        /// </summary>
        public static void ClearStop()
        {
            DriverFolder.ClearStop();
        }

        /// <summary>
        /// Looks for a driver in all the places, in the order of the rules. Runs on a
        /// background thread.
        /// </summary>
        public static IList<DriverCandidate> FindCandidates(
            ProblemDevice device,
            string computerManufacturer,
            Action<string> progress)
        {
            List<DriverCandidate> found = new List<DriverCandidate>();

            found.AddRange(WindowsUpdate.Find(device, progress));

            if (found.Count == 0)
            {
                found.AddRange(UpdateCatalog.Find(device, progress));
            }

            if (found.Count == 0)
            {
                found.AddRange(OemDriverSource.GetCandidates(computerManufacturer));

                if (found.Count > 0)
                {
                    Report(progress, "No driver was found. The official tool of the manufacturer is offered instead.");
                }
            }

            return found;
        }

        /// <summary>
        /// The exact command that is used for a driver. It is shown to the user before
        /// anything is downloaded or installed.
        /// </summary>
        public static string GetInstallCommand(DriverCandidate candidate)
        {
            if (candidate == null)
            {
                return string.Empty;
            }

            switch (candidate.Stage)
            {
                case DriverStage.WindowsUpdate:
                    return "Microsoft.Update.Session: download and install the update \"" +
                           candidate.Name + "\" (Type='Driver')";

                case DriverStage.UpdateCatalog:
                    return "pnputil /add-driver \"<the .inf file from the driver>\" /install";

                default:
                    return "winget install --id \"" + candidate.UpdateId + "\" --exact --silent " +
                           "--accept-package-agreements --accept-source-agreements --disable-interactivity";
            }
        }

        /// <summary>
        /// Installs the driver in an elevated instance of the application and waits
        /// until it has finished. Runs on a background thread.
        /// </summary>
        public static OperationResult Install(DriverCandidate candidate, Action<string> progress)
        {
            OperationResult outcome = new OperationResult();

            if (candidate == null)
            {
                outcome.Message = "No driver was picked.";
                return outcome;
            }

            string requestPath = DriverFolder.GetRequestPath();
            string progressPath = DriverFolder.GetProgressPath();
            string resultPath = DriverFolder.GetResultPath();

            try
            {
                DriverRequest.Write(requestPath, candidate);

                // A sign to stop from an earlier run must not stop this one.
                DriverFolder.ClearStop();

                if (File.Exists(progressPath))
                {
                    File.Delete(progressPath);
                }

                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }
            }
            catch (Exception ex)
            {
                outcome.Message = "The request could not be written. " + ex.Message;
                return outcome;
            }

            string error;
            if (!AdminService.RelaunchAsAdmin(StartupAction.DriverInstaller, out error))
            {
                outcome.Message = string.IsNullOrEmpty(error)
                    ? "The driver could not be started with administrator rights."
                    : error;

                return outcome;
            }

            return WaitForResult(resultPath, progressPath, progress);
        }

        /// <summary>
        /// Waits for the elevated instance to write its result.
        /// </summary>
        private static OperationResult WaitForResult(string resultPath, string progressPath, Action<string> progress)
        {
            OperationResult outcome = new OperationResult();

            DateTime deadline = DateTime.Now.AddMinutes(30);
            long offset = 0;

            while (DateTime.Now < deadline)
            {
                DriverFolder.ReportProgress(progressPath, progress, ref offset);

                if (DriverFolder.IsStopRequested())
                {
                    outcome.Success = false;
                    outcome.Message = "The installation was stopped.";
                    return outcome;
                }

                try
                {
                    if (File.Exists(resultPath))
                    {
                        string content = File.ReadAllText(resultPath).Trim();

                        if (content.Length > 0)
                        {
                            DriverFolder.ReportProgress(progressPath, progress, ref offset);

                            string[] parts = content.Split('|');
                            outcome.Success = parts.Length > 0 && parts[0] == "ok";
                            outcome.Message = parts.Length > 1 ? parts[1] : "The installation has finished.";
                            return outcome;
                        }
                    }
                }
                catch (Exception)
                {
                    // The result file is still being written.
                }

                Thread.Sleep(400);
            }

            outcome.Success = false;
            outcome.Message = "The installation did not finish in time.";
            return outcome;
        }

        /// <summary>
        /// Does the work of the elevated instance. Returns the exit code.
        /// </summary>
        public static int Run(string requestPath)
        {
            try
            {
                // The work of the helper needs administrator rights. When the request
                // was left behind by an earlier run, this is where it ends.
                if (!AdminService.IsElevated())
                {
                    LogService.Warn("The driver helper was started without administrator rights.");
                    DriverFolder.WriteResult(false, "The driver helper has to run with administrator rights.");
                    return -1;
                }

                Dictionary<string, string> request = DriverRequest.Read(requestPath);

                if (request == null)
                {
                    DriverFolder.WriteResult(false, "The request of the driver could not be read.");
                    return -1;
                }

                string error;
                if (!DriverRequest.Validate(request, out error))
                {
                    LogService.Warn("The request of the driver was refused: " + error);
                    DriverFolder.WriteResult(false, error);
                    return -1;
                }

                DriverStage stage;
                DriverRequest.TryGetStage(request["M"], out stage);

                DriverInstallContext context = new DriverInstallContext(
                    delegate(string text) { DriverFolder.WriteStep(DriverFolder.GetProgressPath(), text); },
                    DriverFolder.IsStopRequested);

                DriverFolder.WriteStep(DriverFolder.GetProgressPath(), "The installation has started with administrator rights.");

                if (context.StopIfAsked("The installation was stopped before it began."))
                {
                    DriverFolder.WriteResult(false, "The installation was stopped.");
                    return -1;
                }

                bool success = GetSource(stage).Install(ReadCandidate(request, stage), context);

                DriverFolder.WriteResult(success, success
                    ? "The driver was installed."
                    : "The driver could not be installed.");

                return success ? 0 : 1;
            }
            catch (Exception ex)
            {
                LogService.Error("The driver installation failed.", ex);
                DriverFolder.WriteStep(DriverFolder.GetProgressPath(), "The installation failed. " + ex.Message);
                DriverFolder.WriteResult(false, "The driver could not be installed. " + ex.Message);
                return -1;
            }
        }

        /// <summary>
        /// The place a driver comes from.
        /// </summary>
        private static IDriverSource GetSource(DriverStage stage)
        {
            switch (stage)
            {
                case DriverStage.UpdateCatalog:
                    return UpdateCatalog;

                case DriverStage.OemTool:
                    return OemTool;

                default:
                    return WindowsUpdate;
            }
        }

        /// <summary>
        /// The driver that the request is about.
        /// </summary>
        private static DriverCandidate ReadCandidate(Dictionary<string, string> request, DriverStage stage)
        {
            string id;
            string title;
            string hardwareId;

            request.TryGetValue("U", out id);
            request.TryGetValue("T", out title);
            request.TryGetValue("H", out hardwareId);

            return new DriverCandidate
            {
                Name = string.IsNullOrEmpty(title) ? "the driver" : title,
                UpdateId = id,
                HardwareId = hardwareId,
                Stage = stage
            };
        }

        private static void Report(Action<string> progress, string text)
        {
            if (progress != null && !string.IsNullOrEmpty(text))
            {
                progress(text);
            }
        }
    }
}
