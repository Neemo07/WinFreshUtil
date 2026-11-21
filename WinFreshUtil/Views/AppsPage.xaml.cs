using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using WinFreshUtil.Models;
using WinFreshUtil.Services;

namespace WinFreshUtil.Views
{
    /// <summary>
    /// The list of programs of the WinUtil project. A program is installed with the
    /// package manager of Windows or with Chocolatey.
    /// </summary>
    public partial class AppsPage : UserControl
    {
        private bool _busy;

        /// <summary>
        /// True when the user asked to stop the work. The program that is running is
        /// killed, and the programs that are still waiting are not started.
        /// </summary>
        private volatile bool _stopRequested;
        private bool _useChocolatey;
        private bool _showInstalled;
        private string _filter = "All";
        private string _search = string.Empty;

        /// <summary>How many programs of the run are done.</summary>
        private int _runDone;

        /// <summary>How many programs the run has.</summary>
        private int _runTotal;

        /// <summary>
        /// Waits a moment after the last letter, so that the list is not built for
        /// every letter that is typed.
        /// </summary>
        private readonly DispatcherTimer _searchTimer;

        private readonly Dictionary<string, AppEntry> _entries =
            new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly List<Expander> _groups = new List<Expander>();
        private readonly List<ToggleButton> _tiles = new List<ToggleButton>();

        public AppsPage()
        {
            InitializeComponent();

            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _searchTimer.Tick += delegate
            {
                _searchTimer.Stop();
                BuildList();
            };

            Unloaded += delegate
            {
                _searchTimer.Stop();
            };

            foreach (AppEntry app in AppCatalogService.GetApps())
            {
                _entries[app.Key] = app;
            }

            BuildCategories();
            BuildList();
            CheckManagers();
            LoadInstalled(false);
        }

        #region Building the page

        /// <summary>
        /// The buttons of the groups above the list.
        /// </summary>
        private void BuildCategories()
        {
            CategoryBar.Children.Clear();

            AddCategoryButton("All");

            foreach (string category in AppCatalogService.GetCategories())
            {
                AddCategoryButton(category);
            }
        }

        private void AddCategoryButton(string name)
        {
            RadioButton button = new RadioButton
            {
                Content = name,
                GroupName = "Categories",
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 11,
                Tag = name
            };

            // The first button is picked before the event is connected, so that the
            // list is not built twice when the page opens.
            if (name == "All")
            {
                button.IsChecked = true;
            }

            button.Checked += Category_Click;

            CategoryBar.Children.Add(button);
        }

        /// <summary>
        /// Builds the list of the programs, one group at a time.
        /// </summary>
        private void BuildList()
        {
            CategoryPanel.Children.Clear();
            _groups.Clear();
            _tiles.Clear();

            bool all = _filter == "All";

            foreach (string category in AppCatalogService.GetCategories())
            {
                if (!all && !string.Equals(category, _filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                List<AppEntry> apps = new List<AppEntry>();

                foreach (AppEntry app in AppCatalogService.GetApps())
                {
                    if (!string.Equals(app.Category, category, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (_showInstalled && !app.IsInstalled)
                    {
                        continue;
                    }

                    if (!Matches(app, _search))
                    {
                        continue;
                    }

                    apps.Add(app);
                }

                if (apps.Count == 0)
                {
                    continue;
                }

                Expander expander = new Expander
                {
                    Header = category,
                    IsExpanded = true,
                    Margin = new Thickness(0, 2, 0, 10)
                };

                expander.SetResourceReference(Expander.StyleProperty, "CategoryGroup");

                WrapPanel panel = new WrapPanel();

                foreach (AppEntry app in apps)
                {
                    panel.Children.Add(CreateTile(app));
                }

                expander.Content = panel;

                CategoryPanel.Children.Add(expander);
                _groups.Add(expander);
            }

            if (CategoryPanel.Children.Count == 0)
            {
                TextBlock empty = new TextBlock
                {
                    Text = _showInstalled
                        ? "None of the programs of this list is on the computer."
                        : "There is nothing to show.",
                    Style = (Style)FindResource("HintText")
                };

                CategoryPanel.Children.Add(empty);
            }
        }

        /// <summary>
        /// True when the program fits the text of the search box.
        /// </summary>
        private static bool Matches(AppEntry app, string search)
        {
            if (string.IsNullOrEmpty(search))
            {
                return true;
            }

            if (app.Name != null && app.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (app.Description != null && app.Description.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return app.WingetId != null && app.WingetId.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Shows only the programs whose name fits the text of the search box. The list
        /// is not built for every letter, but a moment after the last one.
        /// </summary>
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _search = SearchBox.Text == null ? string.Empty : SearchBox.Text.Trim();

            if (!IsLoaded)
            {
                return;
            }

            _searchTimer.Stop();
            _searchTimer.Start();
        }

        /// <summary>
        /// The button of one program.
        /// </summary>
        private ToggleButton CreateTile(AppEntry app)
        {
            ToggleButton tile = new ToggleButton
            {
                DataContext = app,
                Content = app.Name,
                ToolTip = app.Description,
                IsChecked = _selected.Contains(app.Key)
            };

            tile.SetResourceReference(Control.StyleProperty, "AppTile");

            tile.Checked += Tile_Checked;
            tile.Unchecked += Tile_Unchecked;

            _tiles.Add(tile);

            return tile;
        }

        /// <summary>
        /// Marks the package managers that are on the computer.
        /// </summary>
        private void CheckManagers()
        {
            bool winget = PackageManagerLocator.IsWingetAvailable();
            bool choco = PackageManagerLocator.IsChocolateyAvailable();

            ChocoOption.IsEnabled = choco;
            ChocoOption.ToolTip = choco ? null : "Chocolatey is not on this computer.";

            WingetOption.IsEnabled = winget;
            WingetOption.ToolTip = winget ? null : "The package manager of Windows is not on this computer.";

            if (!winget && choco)
            {
                ChocoOption.IsChecked = true;
            }

            if (!winget && !choco)
            {
                StatusText.Text = "Neither the package manager of Windows nor Chocolatey is on this computer, " +
                                  "so no program can be installed.";
            }
        }

        /// <summary>
        /// Asks the package manager which programs are on the computer. The answer is
        /// kept, so that the page does not wait for it again. With
        /// <paramref name="refresh"/> the answer is asked for anew.
        /// </summary>
        private async void LoadInstalled(bool refresh)
        {
            try
            {
                ISet<string> installed = await Task.Run(
                    () => AppInstallService.GetInstalledIdsCached(_useChocolatey, refresh));

                foreach (AppEntry app in _entries.Values)
                {
                    app.IsInstalled = (!string.IsNullOrEmpty(app.WingetId) && installed.Contains(app.WingetId)) ||
                                     (!string.IsNullOrEmpty(app.ChocoId) && installed.Contains(app.ChocoId)) ||
                                     (!string.IsNullOrEmpty(app.Name) && installed.Contains(app.Name));
                }

                // The sign on the button of a program changes with the answer itself,
                // so the list only has to be built again when the list of the programs
                // that are on the computer is shown.
                if (_showInstalled)
                {
                    BuildList();
                }
            }
            catch (Exception ex)
            {
                LogService.Error("The list of installed programs could not be read.", ex);
            }
        }

        /// <summary>
        /// Asks for the list of installed programs again.
        /// </summary>
        private void RefreshInstalled_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "The list of the installed programs is asked for again...";

            LoadInstalled(true);
        }

        #endregion

        #region The selection

        private void Tile_Checked(object sender, RoutedEventArgs e)
        {
            AppEntry app = GetApp(sender);
            if (app != null)
            {
                _selected.Add(app.Key);
            }

            UpdateSelectedCount();
        }

        private void Tile_Unchecked(object sender, RoutedEventArgs e)
        {
            AppEntry app = GetApp(sender);
            if (app != null)
            {
                _selected.Remove(app.Key);
            }

            UpdateSelectedCount();
        }

        private static AppEntry GetApp(object sender)
        {
            ToggleButton tile = sender as ToggleButton;
            return tile == null ? null : tile.DataContext as AppEntry;
        }

        private void UpdateSelectedCount()
        {
            SelectedButton.Content = "Selected Apps: " + _selected.Count;
        }

        /// <summary>
        /// The programs the user picked, in the order of the list.
        /// </summary>
        private List<AppEntry> GetSelectedApps()
        {
            List<AppEntry> apps = new List<AppEntry>();

            foreach (AppEntry app in AppCatalogService.GetApps())
            {
                if (_selected.Contains(app.Key))
                {
                    apps.Add(app);
                }
            }

            return apps;
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            _selected.Clear();

            foreach (ToggleButton tile in _tiles)
            {
                tile.IsChecked = false;
            }

            UpdateSelectedCount();
            StatusText.Text = "The selection is empty.";
        }

        private void SelectedApps_Click(object sender, RoutedEventArgs e)
        {
            List<AppEntry> apps = GetSelectedApps();

            if (apps.Count == 0)
            {
                StatusText.Text = "No program is picked.";
                return;
            }

            StringBuilder message = new StringBuilder();

            foreach (AppEntry app in apps)
            {
                message.Append(app.Name);

                if (!string.IsNullOrEmpty(app.WingetId))
                {
                    message.Append("  (").Append(app.WingetId).Append(')');
                }

                message.AppendLine();
            }

            MessageBox.Show(
                Window.GetWindow(this),
                message.ToString(),
                "The picked programs",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (Expander expander in _groups)
            {
                expander.IsExpanded = false;
            }
        }

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (Expander expander in _groups)
            {
                expander.IsExpanded = true;
            }
        }

        private void ShowInstalled_Click(object sender, RoutedEventArgs e)
        {
            _showInstalled = InstalledToggle.IsChecked == true;
            BuildList();
        }

        private void Category_Click(object sender, RoutedEventArgs e)
        {
            RadioButton button = sender as RadioButton;
            if (button == null || button.Tag == null)
            {
                return;
            }

            _filter = Convert.ToString(button.Tag);
            BuildList();
        }

        private void Manager_Changed(object sender, RoutedEventArgs e)
        {
            _useChocolatey = ChocoOption.IsChecked == true;

            if (!IsLoaded)
            {
                return;
            }

            LoadInstalled(true);
        }

        #endregion

        #region Installing

        /// <summary>
        /// Looks for a program in the package manager of Windows, which knows far more
        /// programs than the list of the page.
        /// </summary>
        private async void SearchPackages_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            string query = SearchBox.Text == null ? string.Empty : SearchBox.Text.Trim();

            if (query.Length == 0)
            {
                StatusText.Text = "Type what you are looking for first.";
                return;
            }

            if (!PackageManagerLocator.IsWingetAvailable())
            {
                StatusText.Text = "The package manager of Windows is not on this computer.";
                return;
            }

            SetBusy(true, "The package manager of Windows is looking for \"" + query + "\"...");

            IList<AppPackage> results = await Task.Run(() => AppInstallService.Search(query));

            SetBusy(false, null);

            if (results.Count == 0)
            {
                StatusText.Text = "The package manager of Windows has nothing for \"" + query + "\".";
                return;
            }

            AppPackage chosen = PackageResultsDialog.Show(Window.GetWindow(this), query, results);

            if (chosen == null)
            {
                StatusText.Text = "Cancelled. Nothing was installed.";
                return;
            }

            bool confirmed = ConfirmDialog.Show(
                Window.GetWindow(this),
                "Install " + chosen.Name + "?",
                "The program is installed with the package manager of Windows. It comes from the sources " +
                "that are set up on this computer.",
                AppInstallService.GetInstallCommand(chosen.Id, false),
                "the sources of the package manager of Windows",
                "The package manager may ask for administrator rights.");

            if (!confirmed)
            {
                StatusText.Text = "Cancelled. Nothing was installed.";
                return;
            }

            _stopRequested = false;
            SetBusy(true, "Installing " + chosen.Name + "...");

            Action<AppInstallService.InstallProgress> onProgress = delegate(AppInstallService.InstallProgress progress)
            {
                RunOnUi(delegate
                {
                    if (progress != null && !string.IsNullOrEmpty(progress.Message))
                    {
                        StatusText.Text = progress.Message.Trim();
                    }

                    OverallPanel.Visibility = Visibility.Visible;

                    if (progress != null && progress.Percent >= 0)
                    {
                        OverallBar.IsIndeterminate = false;
                        OverallBar.Value = progress.Percent;
                        OverallText.Text = "Installing " + chosen.Name + "...   " + progress.Percent + "%";
                    }
                    else
                    {
                        OverallBar.IsIndeterminate = true;
                        OverallText.Text = "Installing " + chosen.Name + "...";
                    }
                });
            };

            string output = await Task.Run(() => AppInstallService.Install(chosen, onProgress));

            SetBusy(false, _stopRequested ? "Stopped. " + output : output);
            LogService.Info("The program " + chosen.Id + " was installed: " + output);

            LoadInstalled(true);
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            await RunForApps(false);
        }

        private async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            await RunForApps(true);
        }

        /// <summary>
        /// Installs or removes the picked programs. The window asks first, so that the
        /// user sees what is going to happen. Runs on a background thread.
        /// </summary>
        private async Task RunForApps(bool remove)
        {
            if (_busy)
            {
                return;
            }

            List<AppEntry> apps = GetSelectedApps();

            if (apps.Count == 0)
            {
                StatusText.Text = remove
                    ? "Pick the programs that should be removed first."
                    : "Pick the programs that should be installed first.";

                return;
            }

            StringBuilder message = new StringBuilder();
            StringBuilder commands = new StringBuilder();

            foreach (AppEntry app in apps)
            {
                string id = app.GetPackageId(_useChocolatey);

                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                message.Append(app.Name).AppendLine();
                commands.Append(remove
                    ? AppInstallService.GetUninstallCommand(id, _useChocolatey)
                    : AppInstallService.GetInstallCommand(id, _useChocolatey)).AppendLine();
            }

            if (commands.Length == 0)
            {
                StatusText.Text = "None of the picked programs is in the package manager.";
                return;
            }

            bool confirmed = ConfirmDialog.Show(
                Window.GetWindow(this),
                (remove ? "Remove " : "Install ") + apps.Count + " program(s)?",
                (remove ? "The programs that are removed:" : "The programs that are installed:") +
                "\r\n\r\n" + message,
                commands.ToString().TrimEnd(),
                _useChocolatey ? "the sources of Chocolatey on this computer" : "the sources of the package manager of Windows",
                remove
                    ? "The program is removed from the computer. The files that are left behind are not removed."
                    : "The package manager may ask for administrator rights.");

            if (!confirmed)
            {
                StatusText.Text = remove ? "Cancelled. Nothing was removed." : "Cancelled. Nothing was installed.";
                return;
            }

            _stopRequested = false;
            SetBusy(true, remove ? "The programs are removed..." : "The programs are installed...");

            string result = await Task.Run(() => RunFor(apps, remove));

            SetBusy(false, result);
            LogService.Info((remove ? "The programs were removed: " : "The programs were installed: ") + result);

            LoadInstalled(true);
        }


        private async void UpgradeAll_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            string command = _useChocolatey
                ? "choco upgrade all -y"
                : "winget upgrade --all --silent";

            bool confirmed = ConfirmDialog.Show(
                Window.GetWindow(this),
                "Bring every program up to date?",
                "Every program that has a newer version is brought up to date.",
                command,
                _useChocolatey ? "the sources of Chocolatey on this computer" : "the sources of the package manager of Windows",
                "This can take a long time. The computer may have to be restarted afterwards.");

            if (!confirmed)
            {
                StatusText.Text = "Cancelled. Nothing was changed.";
                return;
            }

            _stopRequested = false;
            SetBusy(true, "The programs are brought up to date...");

            Action<AppInstallService.InstallProgress> onProgress = delegate(AppInstallService.InstallProgress progress)
            {
                RunOnUi(delegate
                {
                    if (progress != null && !string.IsNullOrEmpty(progress.Message))
                    {
                        StatusText.Text = progress.Message.Trim();
                    }

                    OverallPanel.Visibility = Visibility.Visible;
                    OverallText.Text = "Every program that has a newer version is brought up to date...";
                    OverallBar.IsIndeterminate = true;
                });
            };

            string result = await Task.Run(() => AppInstallService.UpgradeAll(_useChocolatey, onProgress));

            SetBusy(false, _stopRequested ? "Stopped. " + result : result);
            LogService.Info("The programs were brought up to date: " + result);

            LoadInstalled(true);
        }

        /// <summary>
        /// Runs a piece of code on the thread of the user interface. It is used by the
        /// work that runs in the background.
        /// </summary>
        private void RunOnUi(Action action)
        {
            if (action == null)
            {
                return;
            }

            // When the window is closed, the dispatcher is no longer there. Calling it
            // then would wait for ever, so the work is left out.
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.Invoke(action);
            }
        }

        /// <summary>
        /// Shows how far one program is.
        /// </summary>
        private void ShowProgress(AppEntry app, AppInstallService.InstallProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            if (progress.Percent >= 0)
            {
                app.IsIndeterminate = false;
                app.Progress = progress.Percent;
                app.ProgressText = progress.Percent + "%";
            }
            else if (string.IsNullOrEmpty(app.ProgressText))
            {
                // Some package managers only print a progress bar on a console, so
                // there is no number to show. The bar still runs as a band.
                app.ProgressText = "Working...";
            }

            if (!string.IsNullOrEmpty(progress.Message))
            {
                StatusText.Text = progress.Message.Trim();
            }

            string text = "Program " + (_runDone + 1) + " of " + _runTotal + ": " + app.Name;

            if (progress.Percent >= 0)
            {
                text += "   " + progress.Percent + "%";
            }

            ShowOverall(text, _runDone, _runTotal);
        }

        /// <summary>
        /// Shows how far the whole run is.
        /// </summary>
        private void ShowOverall(string text, int done, int total)
        {
            OverallPanel.Visibility = Visibility.Visible;
            OverallText.Text = text;
            OverallBar.IsIndeterminate = false;
            OverallBar.Value = total > 0 ? done * 100.0 / total : 0;
        }

        /// <summary>
        /// Installs or removes the programs one after the other. Every program has its
        /// own progress, and the progress of the whole run is shown above. Runs on a
        /// background thread.
        /// </summary>
        private string RunFor(List<AppEntry> apps, bool remove)
        {
            StringBuilder report = new StringBuilder();

            int done = 0;
            int total = apps.Count;

            foreach (AppEntry app in apps)
            {
                if (_stopRequested)
                {
                    break;
                }

                string id = app.GetPackageId(_useChocolatey);

                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                // The program is marked as working, so that its own bar shows.
                RunOnUi(delegate
                {
                    app.Reset();
                    app.IsWorking = true;
                    app.IsIndeterminate = true;
                    app.ProgressText = "Working...";
                });

                _runDone = done;
                _runTotal = total;

                Action<AppInstallService.InstallProgress> onProgress = delegate(AppInstallService.InstallProgress progress)
                {
                    RunOnUi(delegate { ShowProgress(app, progress); });
                };

                string output = remove
                    ? AppInstallService.UninstallById(id, _useChocolatey, onProgress)
                    : AppInstallService.InstallById(id, _useChocolatey, onProgress);

                done++;

                bool worked = output.IndexOf("successfully", StringComparison.OrdinalIgnoreCase) >= 0;

                if (remove)
                {
                    // The program was already gone, which is what was wanted.
                    worked = worked ||
                             output.IndexOf("No installed package found", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                else
                {
                    worked = worked ||
                             output.IndexOf("already installed", StringComparison.OrdinalIgnoreCase) >= 0;
                }

                int finished = done;
                bool stopped = _stopRequested && !worked;

                RunOnUi(delegate
                {
                    app.IsWorking = false;
                    app.IsIndeterminate = false;
                    app.Progress = worked ? 100 : 0;
                    app.ProgressText = worked ? "100%" : (stopped ? "Stopped" : "Failed");

                    if (remove)
                    {
                        app.IsInstalled = false;
                    }
                    else if (worked)
                    {
                        app.IsInstalled = true;
                    }

                    if (!worked && !stopped)
                    {
                        app.HasFailed = true;
                    }

                    ShowOverall(stopped
                        ? "Stopped at " + app.Name
                        : "Finished " + finished + " of " + total + ": " + app.Name, finished, total);
                });

                report.Append(app.Name).Append(": ");
                report.Append(worked ? "done" : "not done");
                report.AppendLine();
            }

            return (_stopRequested ? "Stopped. " : string.Empty) +
                   done + " program(s) were handled. " + report.ToString().TrimEnd();
        }

        /// <summary>
        /// Stops the package manager that is running and the programs that are still
        /// waiting in the run.
        /// </summary>
        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (!_busy)
            {
                return;
            }

            _stopRequested = true;
            StopButton.IsEnabled = false;
            StatusText.Text = "Stopping the work...";

            AppInstallService.KillRunning();
        }

        private void SetBusy(bool busy, string message)
        {
            if (busy && !_busy)
            {
                _stopRequested = false;
            }

            _busy = busy;
            StopButton.IsEnabled = busy && !_stopRequested;

            if (busy)
            {
                // The bar of the whole run is shown again when the first program
                // reports its progress.
                OverallPanel.Visibility = Visibility.Collapsed;
                OverallBar.IsIndeterminate = false;
            }

            if (message != null)
            {
                StatusText.Text = message;
            }
        }

        #endregion
    }
}
