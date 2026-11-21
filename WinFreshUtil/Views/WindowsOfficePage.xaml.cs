using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinFreshUtil.Models;
using WinFreshUtil.Services;
using WinFreshUtil.Theme;

namespace WinFreshUtil.Views
{
    public partial class WindowsOfficePage : UserControl
    {
        // The colours come from the theme, so a card that is made later follows a switch
        // of the theme as well.
        private static Brush CardBorder
        {
            get { return Resource("BorderBrush"); }
        }

        private static Brush OkBrush
        {
            get { return Resource("SuccessBrush"); }
        }

        private static Brush WarnBrush
        {
            get { return Resource("WarningBrush"); }
        }

        private static Brush BadBrush
        {
            get { return Resource("DangerBrush"); }
        }

        private static Brush MutedBrush
        {
            get { return Resource("TextSecondaryBrush"); }
        }

        private static Brush TextBrush
        {
            get { return Resource("TextPrimaryBrush"); }
        }

        private static Brush Resource(string key)
        {
            object value = Application.Current == null ? null : Application.Current.TryFindResource(key);
            return value as Brush ?? Brushes.Transparent;
        }

        private bool _hasScanned;
        private bool _busy;
        private LicenseSnapshot _snapshot;

        public WindowsOfficePage()
        {
            InitializeComponent();
            Loaded += Page_Loaded;
            UpdateMasVersionText();

            // The cards are made in code, so they are made again when the theme changes.
            ThemeManager.Changed += OnThemeChanged;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (_snapshot != null)
            {
                Render(_snapshot);
            }
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hasScanned)
            {
                return;
            }

            _hasScanned = true;
            await RefreshAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            RefreshButton.IsEnabled = false;
            StatusText.Text = "Reading license information...";

            try
            {
                // The WMI and Office licensing tool queries are slow, so they never run on the UI thread.
                LicenseSnapshot snapshot = await Task.Run(() => LicenseService.Scan());

                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                // Push the result onto the WPF dispatcher before touching any control.
                await Dispatcher.InvokeAsync(() => Render(snapshot));
                StatusText.Text = "Last checked: " + DateTime.Now.ToString("t");
                LogService.Info("License information read, " + snapshot.WindowsProducts.Count +
                                " Windows product(s) and " + snapshot.OfficeProducts.Count + " Office product(s).");
            }
            catch (Exception ex)
            {
                StatusText.Text = "The license information could not be read.";
                if (!Dispatcher.HasShutdownStarted)
                {
                    MessageBox.Show(
                        "The license information could not be read.\r\n\r\n" + ex.Message,
                        "WinFreshUtil",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            finally
            {
                RefreshButton.IsEnabled = true;
            }
        }

        private void Render(LicenseSnapshot snapshot)
        {
            _snapshot = snapshot;

            WindowsPanel.Children.Clear();
            OfficePanel.Children.Clear();
            NotesPanel.Children.Clear();

            if (snapshot.WindowsProducts.Count == 0)
            {
                // Only products that carry a product key are reported, so an empty result means
                // "no activated license was found" rather than a proven unlicensed installation.
                WindowsPanel.Children.Add(CreateInfoCard(
                    string.IsNullOrEmpty(snapshot.Error)
                        ? "No activated Windows license was reported for this installation. If Windows is not activated, you can activate it from Settings > System > Activation."
                        : "The Windows license information could not be read."));
            }
            else
            {
                foreach (LicenseEntry entry in snapshot.WindowsProducts)
                {
                    WindowsPanel.Children.Add(CreateProductCard(entry));
                }
            }

            if (snapshot.OfficeProducts.Count == 0)
            {
                OfficePanel.Children.Add(CreateInfoCard(
                    string.IsNullOrEmpty(snapshot.Error)
                        ? "No activated Microsoft Office license was reported for this installation."
                        : "The Microsoft Office license information could not be read."));
            }
            else
            {
                foreach (LicenseEntry entry in snapshot.OfficeProducts)
                {
                    OfficePanel.Children.Add(CreateProductCard(entry));
                }

                if (!string.IsNullOrEmpty(snapshot.OfficeDetectionSource))
                {
                    OfficePanel.Children.Add(CreateCaption("Detected using the " + snapshot.OfficeDetectionSource + "."));
                }
            }

            if (snapshot.FirmwareKeyPresent)
            {
                NotesPanel.Children.Add(CreateBullet(
                    "This device has an OEM product key embedded in its firmware. Activating it is a standard step in Settings > System > Activation."));
            }
            else
            {
                NotesPanel.Children.Add(CreateBullet("No product key was found in the device firmware."));
            }

            foreach (string note in snapshot.Notes)
            {
                NotesPanel.Children.Add(CreateBullet(note));
            }

            if (!string.IsNullOrEmpty(snapshot.Error))
            {
                NotesPanel.Children.Add(CreateBullet(snapshot.Error));
            }
        }

        private async void MasMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            bool confirmed = ConfirmDialog.Show(
                Window.GetWindow(this),
                "Run the Microsoft Activation Scripts?",
                "The script is carried inside this application and started locally, so this step " +
                "needs no internet. It shows the list of functions it offers, and the function that " +
                "is picked afterwards is then run with it.",
                ActivationService.GetCommandLine("MAS_AIO.cmd", string.Empty),
                GetScriptSourceText(),
                "The script needs administrator rights, so Windows will show a UAC prompt. " +
                "It runs in a hidden console window and changes licensing settings on this computer.");

            if (!confirmed)
            {
                ActivationStatusText.Text = "Cancelled. Nothing was run.";
                return;
            }

            SetBusy(true, "Reading the functions that the script offers...");

            MasMenu menu = await Task.Run(() => ActivationService.GetFunctions(null));

            SetBusy(false, null);

            if (menu.Options.Count == 0)
            {
                ActivationStatusText.Text = string.IsNullOrEmpty(menu.Error)
                    ? "The list of functions could not be read. See the log file for the details."
                    : menu.Error;
                return;
            }

            List<MasOption> options = new List<MasOption>();
            foreach (MasOption option in menu.Options)
            {
                if (!option.IsExit && MasFunctionMapper.GetCommand(option.Label) != null)
                {
                    options.Add(option);
                }
            }

            if (options.Count == 0)
            {
                ActivationStatusText.Text = "The script did not offer a function that can be run here.";
                return;
            }

            MasOption chosen = MasSelectionDialog.Show(Window.GetWindow(this), "Choose a function", options);
            if (chosen == null)
            {
                ActivationStatusText.Text = "Cancelled. Nothing was run.";
                return;
            }

            MasCommand command = MasFunctionMapper.GetCommand(chosen.Label);
            if (command == null)
            {
                ActivationStatusText.Text = "That function cannot be run here.";
                return;
            }

            MasTarget target = null;

            if (command.NeedsTarget)
            {
                IList<MasTarget> targets = await Task.Run(() => GetTargets(command));

                if (targets == null || targets.Count == 0)
                {
                    ActivationStatusText.Text = "The possible targets could not be read.";
                    return;
                }

                target = MasTargetDialog.Show(Window.GetWindow(this), command.Name, targets);
                if (target == null)
                {
                    ActivationStatusText.Text = "Cancelled. Nothing was run.";
                    return;
                }
            }

            string warning = "The function needs administrator rights, so Windows will show a UAC prompt. " +
                             "It runs in a hidden console window and changes licensing settings on this computer.";

            bool run = ConfirmDialog.Show(
                Window.GetWindow(this),
                "Run " + command.Name + "?",
                command.Name + " is run by the Microsoft Activation Scripts. " +
                (target == null ? string.Empty : "The target is " + target.Label + ". "),
                ActivationService.GetCommandLine(command.ScriptFile, command.Arguments),
                command.Source,
                warning);

            if (!run)
            {
                ActivationStatusText.Text = "Cancelled. Nothing was run.";
                return;
            }

            SetBusy(true, "Running " + command.Name + "...");

            StringBuilder output = new StringBuilder();

            LogService.Info("The activation function " + command.Name + " was started.");
            ActivationOutcome outcome = await Task.Run(() => ActivationService.RunMasCommand(command, target, delegate(string text)
            {
                output.Clear();
                output.Append(text);
            }));

            string summary = GetSummary(output.ToString());
            SetBusy(false, string.IsNullOrEmpty(summary) ? outcome.Message : outcome.Message + " " + summary);
            LogService.Info("The activation function " + command.Name + " ended. " + outcome.Message);

            await RefreshAsync();
        }

        /// <summary>
        /// Downloads the latest activation scripts from the project. This is the only
        /// step of the section that needs internet; afterwards the new scripts run
        /// locally like the bundled ones.
        /// </summary>
        private async void MasUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            bool confirmed = ConfirmDialog.Show(
                Window.GetWindow(this),
                "Update the activation scripts?",
                "Downloads the latest Microsoft Activation Scripts from GitHub and keeps them " +
                "on this computer. The scripts themselves then run without internet. The current " +
                "scripts stay in place when a file does not arrive completely.",
                ActivationService.RepositoryUrl,
                "Official project repository. The downloaded scripts are stored on this computer " +
                "and run without a connection.",
                "Only the download needs a connection. The files are checked against their hashes " +
                "before they are used.",
                "Address",
                "Update");

            if (!confirmed)
            {
                ActivationStatusText.Text = "Cancelled. Nothing was downloaded.";
                return;
            }

            SetBusy(true, "Updating the activation scripts from GitHub...");

            MasUpdateOutcome outcome = await Task.Run(() => MasUpdateService.Update(null));

            SetBusy(false, outcome.Message);
            UpdateMasVersionText();
            LogService.Info("The activation update ended. " + outcome.Message);
        }

        /// <summary>
        /// Shows which scripts would run and where they came from.
        /// </summary>
        private void UpdateMasVersionText()
        {
            MasVersionText.Text = MasUpdateService.GetVersionText();
        }

        /// <summary>
        /// The source line of the confirmation that starts the script.
        /// </summary>
        private static string GetScriptSourceText()
        {
            string commit = MasUpdateService.GetInstalledCommit();

            if (string.IsNullOrEmpty(commit))
            {
                return "Bundled with this application, no internet needed. Project: " +
                       ActivationService.RepositoryUrl;
            }

            return "Updated from GitHub, stored on this computer. Project: " +
                   ActivationService.RepositoryUrl;
        }

        /// <summary>
        /// Picks the last line of the output that says something, so that the result is
        /// shown without showing the whole output.
        /// </summary>
        private static string GetSummary(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                return string.Empty;
            }

            string[] lines = output.Replace("\r", string.Empty).Split('\n');

            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i].Trim();

                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("_", StringComparison.Ordinal) || line.StartsWith("=", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.IndexOf("press a key", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                if (line.Length > 160)
                {
                    line = line.Substring(0, 160) + "...";
                }

                return line;
            }

            return string.Empty;
        }

        private static IList<MasTarget> GetTargets(MasCommand command)
        {
            if (command.ScriptFile == "Change_Windows_Edition.cmd")
            {
                return ActivationService.GetWindowsEditions(null);
            }

            return MasFunctionMapper.GetOfficeTargets();
        }

        private void SetBusy(bool busy, string message)
        {
            _busy = busy;

            MasMenuButton.IsEnabled = !busy;
            MasUpdateButton.IsEnabled = !busy;
            RefreshButton.IsEnabled = !busy;

            if (busy)
            {
                MasProgressBar.Visibility = Visibility.Visible;
                MasProgressBar.IsIndeterminate = true;
                MasProgressBar.Value = 0;
            }
            else
            {
                MasProgressBar.IsIndeterminate = false;
                MasProgressBar.Value = 100;
                MasProgressBar.Visibility = Visibility.Collapsed;
            }

            if (message != null)
            {
                ActivationStatusText.Text = message;
            }
        }

        private static Border CreateProductCard(LicenseEntry entry)
        {
            StackPanel content = new StackPanel();

            content.Children.Add(new TextBlock
            {
                Text = entry.ProductName,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextBrush
            });

            content.Children.Add(new TextBlock
            {
                Text = "Status: " + entry.StateText,
                FontSize = 12,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = GetStateBrush(entry.State)
            });

            if (!string.IsNullOrEmpty(entry.Description))
            {
                content.Children.Add(new TextBlock
                {
                    Text = entry.Description,
                    FontSize = 12,
                    Margin = new Thickness(0, 3, 0, 0),
                    Foreground = MutedBrush
                });
            }

            if (!string.IsNullOrEmpty(entry.PartialProductKey))
            {
                content.Children.Add(new TextBlock
                {
                    Text = "Product key (last 5 characters): " + entry.PartialProductKey,
                    FontSize = 12,
                    Margin = new Thickness(0, 3, 0, 0),
                    Foreground = MutedBrush
                });
            }

            if (entry.GraceDays.HasValue && entry.GraceDays.Value > 0)
            {
                content.Children.Add(new TextBlock
                {
                    Text = "Remaining grace period: " + entry.GraceDays.Value + " day(s)",
                    FontSize = 12,
                    Margin = new Thickness(0, 3, 0, 0),
                    Foreground = MutedBrush
                });
            }

            return CreateCard(content);
        }

        private static Border CreateInfoCard(string text)
        {
            StackPanel content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap
            });

            return CreateCard(content);
        }

        private static Border CreateCard(UIElement child)
        {
            return new Border
            {
                Background = Resource("SurfaceBrush"),
                BorderBrush = CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 8),
                Child = child
            };
        }

        private static TextBlock CreateBullet(string text)
        {
            return new TextBlock
            {
                Text = "- " + text,
                FontSize = 12,
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 5)
            };
        }

        private static TextBlock CreateCaption(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
        }

        private static Brush GetStateBrush(LicenseState state)
        {
            switch (state)
            {
                case LicenseState.Licensed:
                    return OkBrush;
                case LicenseState.OutOfBoxGrace:
                case LicenseState.OutOfToleranceGrace:
                case LicenseState.ExtendedGrace:
                    return WarnBrush;
                case LicenseState.Unlicensed:
                case LicenseState.Notification:
                case LicenseState.NonGenuineGrace:
                    return BadBrush;
                default:
                    return MutedBrush;
            }
        }
    }
}
