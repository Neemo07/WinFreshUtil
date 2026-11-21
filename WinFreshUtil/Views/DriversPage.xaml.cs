using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WinFreshUtil.Models;
using WinFreshUtil.Services;
using WinFreshUtil.Services.Driver;

namespace WinFreshUtil.Views
{
    public partial class DriversPage : UserControl
    {
        private bool _busy;
        private string _computerManufacturer;

        public DriversPage()
        {
            InitializeComponent();
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            SetBusy(true, "Scanning for driver problems...");
            ResultText.Text = string.Empty;

            // The WMI queries are slow, so they run on a background thread.
            DriverScanResult result = await Task.Run(() => DriverScanner.Scan());

            await Dispatcher.InvokeAsync(() => Render(result));

            SetBusy(false, "Scan finished.");
        }

        private void Render(DriverScanResult result)
        {
            DeviceList.Items.Clear();
            DeviceList.Visibility = Visibility.Collapsed;

            _computerManufacturer = result.ComputerManufacturer;

            SystemText.Text = BuildSystemText(result);
            GraphicsText.Text = BuildGraphicsText(result);

            if (!string.IsNullOrEmpty(result.Error))
            {
                ResultText.Text = result.Error;
                return;
            }

            if (result.Devices.Count == 0)
            {
                ResultText.Text = "No problems were found. Every device that Windows knows about has a working driver.";
                return;
            }

            ResultText.Text = result.Devices.Count == 1
                ? "One device has a problem:"
                : result.Devices.Count + " devices have a problem:";

            foreach (ProblemDevice device in result.Devices)
            {
                DeviceList.Items.Add(device);
            }

            DeviceList.Visibility = Visibility.Visible;
        }

        private static string BuildSystemText(DriverScanResult result)
        {
            string manufacturer = string.IsNullOrEmpty(result.ComputerManufacturer)
                ? "unknown manufacturer"
                : result.ComputerManufacturer;

            string model = string.IsNullOrEmpty(result.ComputerModel)
                ? "unknown model"
                : result.ComputerModel;

            return manufacturer + " " + model;
        }

        private static string BuildGraphicsText(DriverScanResult result)
        {
            if (result.VideoControllers.Count == 0)
            {
                return "No graphics adapter was found.";
            }

            List<string> names = new List<string>(result.VideoControllers);
            return string.Join(Environment.NewLine, names.ToArray());
        }

        /// <summary>
        /// Opens the page of the manufacturer where the drivers of the device are.
        /// </summary>
        private void FindDriver_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }

            ProblemDevice device = button.Tag as ProblemDevice;
            if (device == null)
            {
                return;
            }

            string url = DriverSupportMap.GetSupportUrl(device, _computerManufacturer);

            string error;
            if (!UrlService.TryOpen(url, out error))
            {
                MessageBox.Show(
                    "The support page could not be opened.\r\n\r\n" + error,
                    "WinFreshUtil",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Looks for the driver of the device and installs it. The driver is looked for
        /// in Windows Update first, then in the Microsoft Update Catalog, and the tool
        /// of the manufacturer is offered when nothing was found.
        /// </summary>
        private async void InstallDriver_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }

            ProblemDevice device = button.Tag as ProblemDevice;
            if (device == null)
            {
                return;
            }

            if (_busy)
            {
                return;
            }

            SetBusy(true, "Looking for a driver of " + device.Name + "...");

            IList<DriverCandidate> candidates = await Task.Run(
                () => DriverInstaller.FindCandidates(device, _computerManufacturer, Report));

            SetBusy(false, null);

            if (candidates.Count == 0)
            {
                // Nothing could be found, so the page of the manufacturer is opened and
                // the driver is picked there.
                string url = DriverSupportMap.GetSupportUrl(device, _computerManufacturer);

                string error;
                if (UrlService.TryOpen(url, out error))
                {
                    StatusText.Text = "No driver was found for this device. The page of the manufacturer was opened instead.";
                }
                else
                {
                    StatusText.Text = "No driver was found for this device and the page of the manufacturer could not be opened. " + error;
                }

                return;
            }

            DriverCandidate chosen = DriverChoiceDialog.Show(
                Window.GetWindow(this),
                "Install the driver of " + device.Name + "?",
                "The driver is looked for in Windows Update, then in the Microsoft Update Catalog. " +
                "The driver that is installed is written to the log file.",
                candidates,
                "The driver is installed with administrator rights, so Windows will show a UAC prompt. " +
                "Windows may have to be restarted afterwards.");

            if (chosen == null)
            {
                StatusText.Text = "Cancelled. Nothing was installed.";
                return;
            }

            SetBusy(true, "Installing " + chosen.Name + "...");
            CancelButton.Visibility = Visibility.Visible;

            OperationResult outcome = await Task.Run(() => DriverInstaller.Install(chosen, Report));

            CancelButton.Visibility = Visibility.Collapsed;
            SetBusy(false, outcome.Message);

            LogService.Info("The driver of " + device.Name + " was installed: " + outcome.Message);
            if (!outcome.Success)
            {
                LogService.Warn("The driver of " + device.Name + " was not installed. " + outcome.Message);
            }

            // Look at the device again, the problem is often gone after the driver is there.
            DriverScanResult scan = await Task.Run(() => DriverScanner.Scan());
            await Dispatcher.InvokeAsync(() => Render(scan));
        }

        /// <summary>
        /// Shows a line of the work in the status line. It is called from a background
        /// thread.
        /// </summary>
        private void Report(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            // When the window is closed, the dispatcher is no longer there. Calling it
            // then would wait for ever, so the line is left out.
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            Dispatcher.Invoke(new Action(() => StatusText.Text = message));
        }

        /// <summary>
        /// Asks the installation to stop.
        /// </summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DriverInstaller.RequestStop();
            StatusText.Text = "The installation is asked to stop...";
        }

        private void WindowsUpdate_Click(object sender, RoutedEventArgs e)
        {
            string error;
            if (!UrlService.TryOpen("ms-settings:windowsupdate", out error))
            {
                MessageBox.Show(
                    "The Windows Update settings could not be opened.\r\n\r\n" + error,
                    "WinFreshUtil",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void SetBusy(bool busy, string message)
        {
            _busy = busy;

            ScanButton.IsEnabled = !busy;

            if (busy)
            {
                ScanProgressBar.Visibility = Visibility.Visible;
                ScanProgressBar.IsIndeterminate = true;
            }
            else
            {
                ScanProgressBar.IsIndeterminate = false;
                ScanProgressBar.Value = 100;
                ScanProgressBar.Visibility = Visibility.Collapsed;
            }

            if (message != null)
            {
                StatusText.Text = message;
            }
        }
    }
}
