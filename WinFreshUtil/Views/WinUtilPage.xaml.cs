using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using WinFreshUtil.Services;
using WinFreshUtil.Theme;
using WinFreshUtil.WinUtil;

namespace WinFreshUtil.Views
{
    /// <summary>
    /// The WinUtil section. Every definition comes out of the assembly and every change is
    /// made on this computer; the page itself never opens a connection.
    /// </summary>
    public partial class WinUtilPage : UserControl
    {
        private const string AllChanges = "All changes";

        private enum Tab
        {
            Tweaks,
            Config,
            Updates
        }

        private readonly WinUtilCatalog _catalog;
        private readonly List<RowView> _rows = new List<RowView>();
        private Tab _tab = Tab.Tweaks;
        private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _toggleDesired = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _toggleInitial = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _comboChoices = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _comboInitial = new Dictionary<string, string>(StringComparer.Ordinal);

        private string _dnsChoice = string.Empty;
        private string _dnsInitial = string.Empty;
        private string _powerPlan = string.Empty;
        private bool _loading = true;
        private bool _busy;
        private int _logLines;

        public WinUtilPage()
        {
            InitializeComponent();

            _catalog = WinUtilCatalogLoader.Load();

            BuildFilters();
            BuildSections();

            _loading = false;
            UpdateSelectionCount();

            // The cards are made in code, so they are made again when the theme changes.
            ThemeManager.Changed += OnThemeChanged;

            AddLog(new WinUtilLogLine(
                "The WinUtil definitions are loaded from inside this program. No connection is used.",
                WinUtilLogLevel.Info));
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            double offset = ContentScroll.VerticalOffset;
            BuildSections();
            ContentScroll.ScrollToVerticalOffset(offset);
        }

        /// <summary>
        /// One entry that is shown. A row is either a tweak or feature, or one of the
        /// Windows applications that can be removed.
        /// </summary>
        private sealed class RowView
        {
            public WinUtilEntry Entry;
            public WinUtilAppxEntry App;
            public CheckBox Check;
            public ComboBox Combo;
            public TextBlock State;

            public string Key
            {
                get { return Entry != null ? Entry.Key : (App != null ? App.Key : string.Empty); }
            }
        }

        private sealed class NamedChoice
        {
            public string Key { get; set; }
            public string Title { get; set; }

            public override string ToString()
            {
                return Title;
            }
        }

        // ---------------------------------------------------------------- filters

        private void BuildFilters()
        {
            _loading = true;

            FillCategories();
            FillPresets();

            _loading = false;
        }

        /// <summary>
        /// The categories of the tab that is open. The Updates tab has none, because its
        /// three profiles are always shown.
        /// </summary>
        private void FillCategories()
        {
            CategoryBox.Items.Clear();
            CategoryBox.Items.Add(AllChanges);
            CategoryBox.SelectedIndex = 0;

            if (_tab == Tab.Updates)
            {
                CategoryBox.IsEnabled = false;
                return;
            }

            CategoryBox.IsEnabled = true;

            List<string> categories = new List<string>();

            if (_tab == Tab.Tweaks)
            {
                foreach (WinUtilEntry entry in _catalog.Tweaks)
                {
                    AddCategory(categories, entry.Category);
                }
            }
            else
            {
                foreach (WinUtilEntry entry in _catalog.Features)
                {
                    AddCategory(categories, entry.Category);
                }

                foreach (WinUtilAppxEntry app in _catalog.Apps)
                {
                    AddCategory(categories, app.Category);
                }
            }

            foreach (string category in categories)
            {
                CategoryBox.Items.Add(category);
            }
        }

        /// <summary>
        /// A button for every preset. A preset picks its changes at once, so the button is
        /// the whole action and there is nothing else to press.
        /// </summary>
        private void FillPresets()
        {
            PresetBar.Children.Clear();

            PresetBar.Children.Add(new TextBlock
            {
                Text = "Presets",
                FontSize = 11,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            foreach (string name in new[] { "Standard", "Minimal", "Advanced" })
            {
                List<string> keys;
                if (!_catalog.Presets.TryGetValue(name, out keys))
                {
                    continue;
                }

                Button button = new Button
                {
                    Content = name,
                    Style = (Style)FindResource("ActionButton"),
                    MinWidth = 78,
                    MinHeight = 28,
                    FontSize = 11,
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(0, 0, 6, 0),
                    Tag = name,
                    ToolTip = "Select the changes of the " + name + " preset"
                };

                AutomationProperties.SetName(button, "Use the " + name + " preset");
                button.Click += Preset_Click;
                PresetBar.Children.Add(button);
            }
        }

        private static void AddCategory(List<string> categories, string category)
        {
            string name = string.IsNullOrEmpty(category) ? "Other" : category;
            if (!categories.Contains(name))
            {
                categories.Add(name);
            }
        }

        // ---------------------------------------------------------------- sections

        private void BuildSections()
        {
            SectionsPanel.Children.Clear();
            _rows.Clear();

            // The presets are groups of tweaks, so they are shown on the Tweaks tab only.
            PresetBar.Visibility = _tab == Tab.Tweaks ? Visibility.Visible : Visibility.Collapsed;

            if (_tab == Tab.Updates)
            {
                SectionsPanel.Visibility = Visibility.Collapsed;
                UpdatesPanel.Visibility = Visibility.Visible;
                BuildUpdates();
                return;
            }

            SectionsPanel.Visibility = Visibility.Visible;
            UpdatesPanel.Visibility = Visibility.Collapsed;

            string category = CategoryBox.SelectedItem as string ?? AllChanges;
            string search = (SearchBox.Text ?? string.Empty).Trim();
            bool all = string.Equals(category, AllChanges, StringComparison.Ordinal);

            int shown = 0;

            if (_tab == Tab.Tweaks)
            {
                if (all && search.Length == 0)
                {
                    AddDnsCard();
                }

                shown += AddEntryGroups("Changes", _catalog.Tweaks, category, search, false);
                shown += AddPowerPlan(category, search);
            }
            else
            {
                shown += AddEntryGroups("Windows features", _catalog.Features, category, search, true);
                shown += AddAppGroups("Windows applications", _catalog.Apps, category, search);
            }

            if (shown == 0)
            {
                TextBlock empty = new TextBlock
                {
                    Text = "No change matches this search and category.",
                    Style = (Style)FindResource("HintText"),
                    Margin = new Thickness(2, 20, 0, 0)
                };

                SectionsPanel.Children.Add(empty);
            }
        }

        private int AddEntryGroups(
            string heading,
            List<WinUtilEntry> entries,
            string category,
            string search,
            bool feature)
        {
            List<string> order = new List<string>();
            Dictionary<string, List<WinUtilEntry>> groups =
                new Dictionary<string, List<WinUtilEntry>>(StringComparer.Ordinal);

            foreach (WinUtilEntry entry in entries)
            {
                // The two halves of the power plan are one choosable card below.
                if (IsPowerPlanKey(entry.Key))
                {
                    continue;
                }

                string name = string.IsNullOrEmpty(entry.Category) ? "Other" : entry.Category;

                if (!string.Equals(category, AllChanges, StringComparison.Ordinal) &&
                    !string.Equals(category, name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Matches(entry.Content, entry.Description, entry.Key, name, search))
                {
                    continue;
                }

                if (!groups.ContainsKey(name))
                {
                    groups[name] = new List<WinUtilEntry>();
                    order.Add(name);
                }

                groups[name].Add(entry);
            }

            int shown = 0;

            foreach (string name in order)
            {
                AddSectionHeading(feature ? heading + " / " + DisplayCategory(name) : DisplayCategory(name),
                    groups[name].Count, name.StartsWith("z__", StringComparison.Ordinal));

                foreach (WinUtilEntry entry in groups[name])
                {
                    AddEntryRow(entry);
                    shown++;
                }
            }

            return shown;
        }

        private int AddAppGroups(
            string heading,
            List<WinUtilAppxEntry> apps,
            string category,
            string search)
        {
            List<string> order = new List<string>();
            Dictionary<string, List<WinUtilAppxEntry>> groups =
                new Dictionary<string, List<WinUtilAppxEntry>>(StringComparer.Ordinal);

            foreach (WinUtilAppxEntry app in apps)
            {
                string name = string.IsNullOrEmpty(app.Category) ? "Other" : app.Category;

                if (!string.Equals(category, AllChanges, StringComparison.Ordinal) &&
                    !string.Equals(category, name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Matches(app.Content, app.Description, app.Key, name, search))
                {
                    continue;
                }

                if (!groups.ContainsKey(name))
                {
                    groups[name] = new List<WinUtilAppxEntry>();
                    order.Add(name);
                }

                groups[name].Add(app);
            }

            int shown = 0;

            foreach (string name in order)
            {
                AddSectionHeading(heading + " / " + DisplayCategory(name), groups[name].Count, false);

                foreach (WinUtilAppxEntry app in groups[name])
                {
                    AddAppRow(app);
                    shown++;
                }
            }

            return shown;
        }

        private static bool Matches(string content, string description, string key, string category, string search)
        {
            if (string.IsNullOrEmpty(search))
            {
                return true;
            }

            return Contains(content, search) || Contains(description, search) ||
                   Contains(key, search) || Contains(category, search);
        }

        private static bool Contains(string text, string search)
        {
            return !string.IsNullOrEmpty(text) &&
                   text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DisplayCategory(string category)
        {
            string text = (category ?? string.Empty).Replace("z__", string.Empty);
            return text.Replace(" - CAUTION", " (caution)");
        }

        private void AddSectionHeading(string title, int count, bool caution)
        {
            Grid grid = new Grid { Margin = new Thickness(2, 16, 2, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock name = new TextBlock
            {
                Text = title,
                Style = (Style)FindResource("SectionTitle"),
                Margin = new Thickness(0)
            };

            TextBlock amount = new TextBlock
            {
                Text = count == 1 ? "1 item" : count + " items",
                Style = (Style)FindResource("CaptionText"),
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(amount, 1);
            grid.Children.Add(name);
            grid.Children.Add(amount);
            SectionsPanel.Children.Add(grid);

            if (caution)
            {
                TextBlock warning = new TextBlock
                {
                    Text = "These changes can affect Windows in ways that are hard to undo. Read the text of each change before you apply it.",
                    Style = (Style)FindResource("HintText"),
                    Margin = new Thickness(2, 0, 2, 8)
                };

                warning.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
                SectionsPanel.Children.Add(warning);
            }
        }

        private void AddDnsCard()
        {
            Border card = new Border
            {
                Style = (Style)FindResource("Card"),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 2, 0, 6)
            };

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = "DNS - Set to:",
                Style = (Style)FindResource("SectionTitle"),
                Margin = new Thickness(0, 0, 0, 3)
            });

            text.Children.Add(new TextBlock
            {
                Text = "The name servers of the active network interfaces. The settings of Windows are changed; this program does not contact the servers.",
                Style = (Style)FindResource("HintText"),
                MaxWidth = 620,
                HorizontalAlignment = HorizontalAlignment.Left
            });

            ComboBox combo = new ComboBox
            {
                Width = 270,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0)
            };

            combo.Items.Add(new NamedChoice { Key = string.Empty, Title = "Leave the name servers as they are" });
            combo.Items.Add(new NamedChoice { Key = "DHCP", Title = "From DHCP" });

            foreach (WinUtilDnsProvider provider in _catalog.DnsProviders)
            {
                combo.Items.Add(new NamedChoice { Key = provider.Key, Title = provider.Title });
            }

            int selected = 0;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                NamedChoice choice = combo.Items[i] as NamedChoice;
                if (choice != null && string.Equals(choice.Key, _dnsChoice, StringComparison.Ordinal))
                {
                    selected = i;
                    break;
                }
            }

            combo.SelectedIndex = selected;
            combo.SelectionChanged += delegate
            {
                NamedChoice choice = combo.SelectedItem as NamedChoice;
                _dnsChoice = choice == null ? string.Empty : choice.Key;
                UpdateSelectionCount();
            };

            Grid.SetColumn(combo, 1);
            grid.Children.Add(text);
            grid.Children.Add(combo);
            card.Child = grid;
            SectionsPanel.Children.Add(card);
        }

        /// <summary>
        /// One card for the Ultimate Performance plan. The enable and the disable change
        /// are the two answers of one question, so they are picked from a list instead of
        /// two rows with a button of their own.
        /// </summary>
        private int AddPowerPlan(string category, string search)
        {
            const string planCategory = "Performance Plans - NOT FOR LAPTOPS";
            const string title = "Ultimate Performance Profile";
            const string description =
                "Enables or disables the Ultimate Performance power plan of Windows. The plan is made for a desktop computer, not a laptop.";

            if (!string.Equals(category, AllChanges, StringComparison.Ordinal) &&
                !string.Equals(category, planCategory, StringComparison.Ordinal))
            {
                return 0;
            }

            if (!Matches(title, description, "WPFAddUltPerf", planCategory, search))
            {
                return 0;
            }

            AddSectionHeading(DisplayCategory(planCategory), 1, false);

            Border card = new Border
            {
                Style = (Style)FindResource("Card"),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 2, 0, 6)
            };

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = title,
                Style = (Style)FindResource("SectionTitle"),
                Margin = new Thickness(0, 0, 0, 3)
            });

            text.Children.Add(new TextBlock
            {
                Text = description,
                Style = (Style)FindResource("HintText"),
                MaxWidth = 620,
                HorizontalAlignment = HorizontalAlignment.Left
            });

            ComboBox combo = new ComboBox
            {
                Width = 210,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0)
            };

            combo.Items.Add(new NamedChoice { Key = string.Empty, Title = "Leave the plan as it is" });
            combo.Items.Add(new NamedChoice { Key = "WPFAddUltPerf", Title = "Enable" });
            combo.Items.Add(new NamedChoice { Key = "WPFRemoveUltPerf", Title = "Disable" });

            int selected = 0;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                NamedChoice choice = combo.Items[i] as NamedChoice;
                if (choice != null && string.Equals(choice.Key, _powerPlan, StringComparison.Ordinal))
                {
                    selected = i;
                    break;
                }
            }

            combo.SelectedIndex = selected;
            combo.SelectionChanged += delegate
            {
                NamedChoice choice = combo.SelectedItem as NamedChoice;
                _powerPlan = choice == null ? string.Empty : choice.Key;
                UpdateSelectionCount();
            };

            AutomationProperties.SetName(combo, "Ultimate Performance Profile - Set to:");

            Grid.SetColumn(combo, 1);
            grid.Children.Add(text);
            grid.Children.Add(combo);
            card.Child = grid;
            SectionsPanel.Children.Add(card);

            return 1;
        }

        private static bool IsPowerPlanKey(string key)
        {
            return string.Equals(key, "WPFAddUltPerf", StringComparison.Ordinal) ||
                   string.Equals(key, "WPFRemoveUltPerf", StringComparison.Ordinal);
        }

        // ---------------------------------------------------------------- updates
        /// <summary>
        /// The three update profiles of the original project, with the same texts.
        /// </summary>
        private void BuildUpdates()
        {
            UpdatesPanel.Children.Clear();

            UpdatesPanel.Children.Add(new TextBlock
            {
                Text = "Windows Update Profiles",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(1, 2, 0, 0)
            });

            UpdatesPanel.Children.Add(new TextBlock
            {
                Text = "Choose how Windows receives updates. Each profile replaces the Windows Update settings managed by WinUtil.",
                Style = (Style)FindResource("HintText"),
                Margin = new Thickness(1, 6, 0, 0),
                MaxWidth = 780,
                HorizontalAlignment = HorizontalAlignment.Left
            });

            UniformGrid grid = new UniformGrid
            {
                Columns = 3,
                Margin = new Thickness(0, 14, 0, 0)
            };

            grid.Children.Add(CreateUpdateCard(
                "Recommended",
                "Balanced security and stability",
                false,
                new[]
                {
                    "Defers feature updates for 365 days",
                    "Defers quality updates for 4 days",
                    "Excludes drivers from quality updates",
                    "Notifies when downloaded updates are ready to install"
                },
                "Available on Windows Pro, Enterprise, and Education editions.",
                "Apply Recommended",
                "WPFUpdatessecurity",
                true));

            grid.Children.Add(CreateUpdateCard(
                "Windows Default",
                "Return control to Windows",
                false,
                new[]
                {
                    "Removes Windows Update policies applied by WinUtil",
                    "Restores update service startup settings",
                    "Re-enables update scheduled tasks"
                },
                "Use this to undo the Recommended or Disable profile.",
                "Restore Defaults",
                "WPFUpdatesdefault",
                false));

            grid.Children.Add(CreateUpdateCard(
                "Disable Updates",
                "Advanced use only",
                true,
                new[]
                {
                    "Disables automatic update policy",
                    "Stops update services and scheduled tasks",
                    "Clears downloaded update files"
                },
                "Security updates will not be installed while this profile is active.",
                "Disable Updates",
                "WPFUpdatesdisable",
                false));

            UpdatesPanel.Children.Add(grid);

            Border note = new Border
            {
                Style = (Style)FindResource("Card"),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 12, 0, 0)
            };

            note.Child = new TextBlock
            {
                Text = "Changes apply system-wide. Restart Windows after switching profiles. Use Restore Defaults to undo WinUtil update policies.",
                Style = (Style)FindResource("HintText"),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            UpdatesPanel.Children.Add(note);
        }

        private Border CreateUpdateCard(
            string title,
            string subtitle,
            bool danger,
            string[] lines,
            string note,
            string buttonText,
            string key,
            bool primary)
        {
            Border card = new Border
            {
                Style = (Style)FindResource("Card"),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 10, 10),
                MinHeight = 300
            };

            DockPanel panel = new DockPanel { LastChildFill = true };

            Button action = new Button
            {
                Content = buttonText,
                Style = (Style)FindResource(primary ? "PrimaryButton" : "ActionButton"),
                Tag = key,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 16, 0, 0)
            };

            if (danger)
            {
                action.SetResourceReference(Button.ForegroundProperty, "DangerBrush");
                action.SetResourceReference(Button.BorderBrushProperty, "DangerBrush");
            }

            AutomationProperties.SetName(action, buttonText);
            action.Click += UpdateProfile_Click;
            DockPanel.SetDock(action, Dock.Bottom);
            panel.Children.Add(action);

            StackPanel text = new StackPanel();

            TextBlock heading = new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold
            };

            if (danger)
            {
                heading.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            }

            text.Children.Add(heading);

            TextBlock under = new TextBlock
            {
                Text = subtitle,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 14),
                TextWrapping = TextWrapping.Wrap
            };

            if (danger)
            {
                under.FontWeight = FontWeights.SemiBold;
                under.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            }
            else
            {
                under.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            }

            text.Children.Add(under);

            foreach (string line in lines)
            {
                text.Children.Add(new TextBlock
                {
                    Text = "- " + line,
                    Style = (Style)FindResource("BodyText"),
                    Margin = new Thickness(0, 0, 0, 7)
                });
            }

            TextBlock small = new TextBlock
            {
                Text = note,
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            };

            small.SetResourceReference(
                TextBlock.ForegroundProperty,
                danger ? "DangerBrush" : "TextMutedBrush");

            text.Children.Add(small);

            panel.Children.Add(text);
            card.Child = panel;
            return card;
        }

        private async void UpdateProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            Button button = sender as Button;
            string key = button == null ? null : button.Tag as string;
            WinUtilEntry entry = _catalog.FindEntry(key);
            if (entry == null)
            {
                return;
            }

            WinUtilSelection selection = new WinUtilSelection();
            selection.Actions.Add(new WinUtilAction { Key = entry.Key, Undo = false });

            if (!Confirm(selection, false))
            {
                StatusText.Text = "Cancelled. Nothing was changed.";
                return;
            }

            await RunAsync(selection, entry.ButtonText + "...");
        }

        // ---------------------------------------------------------------- rows

        private void AddEntryRow(WinUtilEntry entry)
        {
            RowView row = new RowView { Entry = entry };

            Border card = CreateCard();
            Grid grid = CreateRowGrid();

            StackPanel info = new StackPanel();
            info.Children.Add(CreateTitle(entry.Content, entry.Key));
            info.Children.Add(CreateDescription(entry.Description));

            TextBlock state = new TextBlock
            {
                Style = (Style)FindResource("CaptionText"),
                Margin = new Thickness(0, 5, 0, 0),
                Visibility = Visibility.Collapsed
            };

            info.Children.Add(state);
            row.State = state;

            Control control = null;

            switch (entry.Kind)
            {
                case WinUtilEntryKind.Toggle:
                    bool current = WinUtilState.IsApplied(entry);
                    _toggleInitial[entry.Key] = current;

                    bool desired;
                    if (!_toggleDesired.TryGetValue(entry.Key, out desired))
                    {
                        desired = current;
                    }

                    CheckBox toggle = new CheckBox
                    {
                        Style = (Style)FindResource("ToggleSwitch"),
                        IsChecked = desired,
                        Tag = entry.Key,
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = entry.Description
                    };

                    AutomationProperties.SetName(toggle, entry.Content);

                    toggle.Checked += Toggle_Changed;
                    toggle.Unchecked += Toggle_Changed;
                    row.Check = toggle;
                    control = toggle;

                    state.Visibility = Visibility.Visible;
                    state.Text = desired ? "On" : "Off";
                    break;

                case WinUtilEntryKind.ComboBox:
                    string initialChoice = WinUtilState.FindComboChoice(entry) ?? string.Empty;
                    _comboInitial[entry.Key] = initialChoice;

                    string chosen;
                    if (!_comboChoices.TryGetValue(entry.Key, out chosen))
                    {
                        chosen = initialChoice;
                    }

                    ComboBox combo = new ComboBox
                    {
                        Width = 200,
                        Tag = entry.Key,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    AutomationProperties.SetName(combo, entry.Content);

                    foreach (string item in entry.ComboItems)
                    {
                        combo.Items.Add(item);
                    }

                    if (!string.IsNullOrEmpty(chosen) && combo.Items.Contains(chosen))
                    {
                        combo.SelectedItem = chosen;
                    }
                    else if (combo.Items.Count > 0)
                    {
                        combo.SelectedIndex = 0;
                    }

                    combo.SelectionChanged += Combo_Changed;
                    row.Combo = combo;
                    control = combo;

                    state.Visibility = Visibility.Visible;
                    state.Text = ComboDescription(entry, combo.SelectedItem as string);
                    break;

                case WinUtilEntryKind.Button:
                    Button button = new Button
                    {
                        Content = entry.IsFeature &&
                                  !string.Equals(entry.Key, "WPFPanelDISM", StringComparison.Ordinal) &&
                                  (entry.Key.StartsWith("WPFPanel", StringComparison.Ordinal) ||
                                   string.Equals(entry.Key, "WPFWinUtilSSHServer", StringComparison.Ordinal))
                            ? "Open"
                            : "Run",
                        Style = (Style)FindResource("ActionButton"),
                        MinWidth = 70,
                        Tag = entry.Key,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    button.Click += Button_Click;
                    control = button;
                    break;

                default:
                    CheckBox box = new CheckBox
                    {
                        IsChecked = _selected.Contains(entry.Key),
                        Tag = entry.Key,
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = entry.Content
                    };

                    box.Checked += Selection_Changed;
                    box.Unchecked += Selection_Changed;
                    row.Check = box;
                    control = box;

                    AutomationProperties.SetName(box, "Select " + entry.Content);
                    break;
            }

            if (control != null)
            {
                control.Margin = new Thickness(18, 0, 0, 0);
                Grid.SetColumn(control, 1);
                grid.Children.Add(control);
            }

            grid.Children.Add(info);
            card.Child = grid;
            SectionsPanel.Children.Add(card);
            _rows.Add(row);
        }

        private void AddAppRow(WinUtilAppxEntry app)
        {
            RowView row = new RowView { App = app };

            Border card = CreateCard();
            Grid grid = CreateRowGrid();

            StackPanel info = new StackPanel();
            info.Children.Add(CreateTitle(app.Content, app.Key));

            string description = app.Description;
            if (string.IsNullOrEmpty(description))
            {
                description = "Removes the application for all users of this computer.";
            }

            info.Children.Add(CreateDescription(description));

            CheckBox box = new CheckBox
            {
                IsChecked = _selected.Contains(app.Key),
                Tag = app.Key,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Remove " + app.Content
            };

            box.Checked += Selection_Changed;
            box.Unchecked += Selection_Changed;
            row.Check = box;

            AutomationProperties.SetName(box, "Remove " + app.Content);

            box.Margin = new Thickness(18, 0, 0, 0);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            grid.Children.Add(info);
            card.Child = grid;
            SectionsPanel.Children.Add(card);
            _rows.Add(row);
        }

        private static Border CreateCard()
        {
            Border card = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 11, 14, 11),
                Margin = new Thickness(0, 0, 0, 7)
            };

            card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            return card;
        }

        private static Style StyleOf(string key)
        {
            return Application.Current == null ? null : Application.Current.TryFindResource(key) as Style;
        }

        private static Grid CreateRowGrid()
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            return grid;
        }

        private static TextBlock CreateTitle(string text, string key)
        {
            TextBlock title = new TextBlock
            {
                Text = string.IsNullOrEmpty(text) ? key : text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };

            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            return title;
        }

        private static TextBlock CreateDescription(string text)
        {
            TextBlock description = new TextBlock
            {
                Text = text ?? string.Empty,
                Style = StyleOf("HintText"),
                Margin = new Thickness(0, 3, 12, 0)
            };

            if (string.IsNullOrEmpty(text))
            {
                description.Visibility = Visibility.Collapsed;
            }

            return description;
        }

        private static string ComboDescription(WinUtilEntry entry, string choice)
        {
            if (string.IsNullOrEmpty(choice))
            {
                return string.Empty;
            }

            string description;
            if (entry.ComboDescriptions.TryGetValue(choice, out description))
            {
                return description;
            }

            return string.Empty;
        }

        // ---------------------------------------------------------------- selection

        private void Selection_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            CheckBox box = sender as CheckBox;
            string key = box == null ? null : box.Tag as string;
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (box.IsChecked == true)
            {
                _selected.Add(key);
            }
            else
            {
                _selected.Remove(key);
            }

            UpdateSelectionCount();
        }

        private void Toggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            CheckBox box = sender as CheckBox;
            string key = box == null ? null : box.Tag as string;
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            bool on = box.IsChecked == true;
            _toggleDesired[key] = on;

            foreach (RowView row in _rows)
            {
                if (row.Entry != null && string.Equals(row.Entry.Key, key, StringComparison.Ordinal) &&
                    row.State != null)
                {
                    row.State.Text = on ? "On (not applied yet)" : "Off (not applied yet)";
                }
            }

            UpdateSelectionCount();
        }

        private void Combo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            ComboBox combo = sender as ComboBox;
            string key = combo == null ? null : combo.Tag as string;
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            string choice = combo.SelectedItem as string;
            _comboChoices[key] = choice ?? string.Empty;

            foreach (RowView row in _rows)
            {
                if (row.Entry != null && string.Equals(row.Entry.Key, key, StringComparison.Ordinal) &&
                    row.State != null)
                {
                    row.State.Text = ComboDescription(row.Entry, choice);
                }
            }

            UpdateSelectionCount();
        }

        private void UpdateSelectionCount()
        {
            int count = _selected.Count;

            foreach (KeyValuePair<string, bool> item in _toggleInitial)
            {
                bool desired;
                if (_toggleDesired.TryGetValue(item.Key, out desired) && desired != item.Value)
                {
                    count++;
                }
            }

            foreach (KeyValuePair<string, string> item in _comboChoices)
            {
                string initial;
                if (_comboInitial.TryGetValue(item.Key, out initial) &&
                    !string.Equals(initial, item.Value, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            if (!string.IsNullOrEmpty(_dnsChoice) &&
                !string.Equals(_dnsChoice, "Default", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_dnsChoice, _dnsInitial, StringComparison.Ordinal))
            {
                count++;
            }

            if (HasPowerPlanChoice())
            {
                count++;
            }

            SelectionText.Text = count == 0 ? "Nothing selected" : count + " change(s) ready";

            if (!_busy)
            {
                ApplyButton.IsEnabled = count > 0;
                UndoButton.IsEnabled = HasUndoableSelection();
            }
        }

        private bool HasUndoableSelection()
        {
            if (_selected.Count == 0)
            {
                return false;
            }

            foreach (string key in _selected)
            {
                WinUtilEntry entry = _catalog.FindTweak(key);
                if (entry != null && (entry.Registry.Count > 0 || entry.Services.Count > 0 || entry.UndoScripts.Count > 0))
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            _selected.Clear();
            _toggleDesired.Clear();
            _comboChoices.Clear();
            _dnsChoice = string.Empty;
            _powerPlan = string.Empty;
            BuildSections();
            UpdateSelectionCount();
            StatusText.Text = "The selection is cleared.";
        }

        private bool HasPowerPlanChoice()
        {
            return !string.IsNullOrEmpty(_powerPlan);
        }

        /// <summary>
        /// A preset picks the changes of its group at once. Anything that the previous
        /// preset picked is dropped, so the group is exactly what the name says.
        /// </summary>
        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            string name = button == null ? null : button.Tag as string;
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            List<string> keys;
            if (!_catalog.Presets.TryGetValue(name, out keys))
            {
                return;
            }

            if (_tab != Tab.Tweaks)
            {
                SwitchTab(Tab.Tweaks);
            }

            List<string> remove = new List<string>();
            foreach (string key in _selected)
            {
                if (key.StartsWith("WPFTweaks", StringComparison.Ordinal))
                {
                    remove.Add(key);
                }
            }

            foreach (string key in remove)
            {
                _selected.Remove(key);
            }

            int added = 0;
            foreach (string key in keys)
            {
                if (_catalog.FindTweak(key) != null)
                {
                    _selected.Add(key);
                    added++;
                }
            }

            BuildSections();
            UpdateSelectionCount();
            StatusText.Text = "The " + name + " preset selected " + added + " change(s).";
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading || _catalog == null)
            {
                return;
            }

            Tab tab = _tab;
            if (sender == TabConfig)
            {
                tab = Tab.Config;
            }
            else if (sender == TabUpdates)
            {
                tab = Tab.Updates;
            }
            else if (sender == TabTweaks)
            {
                tab = Tab.Tweaks;
            }

            if (tab == _tab)
            {
                return;
            }

            _tab = tab;

            _loading = true;
            FillCategories();
            _loading = false;

            ContentScroll.ScrollToTop();
            BuildSections();
            UpdateSelectionCount();
        }

        /// <summary>
        /// Opens a tab without going through the click of the user, so that a search can
        /// show the tab that holds the match.
        /// </summary>
        private void SwitchTab(Tab tab)
        {
            _loading = true;

            _tab = tab;

            if (tab == Tab.Tweaks)
            {
                TabTweaks.IsChecked = true;
            }
            else if (tab == Tab.Config)
            {
                TabConfig.IsChecked = true;
            }
            else
            {
                TabUpdates.IsChecked = true;
            }

            FillCategories();

            _loading = false;

            ContentScroll.ScrollToTop();
            BuildSections();
            UpdateSelectionCount();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            SearchBox.Focus();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool empty = string.IsNullOrEmpty(SearchBox.Text);

            if (SearchPlaceholder != null)
            {
                SearchPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ClearSearchButton != null)
            {
                ClearSearchButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            }

            if (_loading)
            {
                return;
            }

            // A match in another tab is shown there, so that the search is not limited to
            // the tab that happens to be open.
            string search = (SearchBox.Text ?? string.Empty).Trim();
            if (search.Length > 0 && !TabHasMatch(_tab, search))
            {
                Tab other = TabWithMatch(search);
                if (other != _tab)
                {
                    SwitchTab(other);
                    return;
                }
            }

            BuildSections();
        }

        private bool TabHasMatch(Tab tab, string search)
        {
            if (tab == Tab.Updates)
            {
                return false;
            }

            if (tab == Tab.Tweaks)
            {
                foreach (WinUtilEntry entry in _catalog.Tweaks)
                {
                    if (Matches(entry.Content, entry.Description, entry.Key, entry.Category, search))
                    {
                        return true;
                    }
                }

                return false;
            }

            foreach (WinUtilEntry entry in _catalog.Features)
            {
                if (Matches(entry.Content, entry.Description, entry.Key, entry.Category, search))
                {
                    return true;
                }
            }

            foreach (WinUtilAppxEntry app in _catalog.Apps)
            {
                if (Matches(app.Content, app.Description, app.Key, app.Category, search))
                {
                    return true;
                }
            }

            return false;
        }

        private Tab TabWithMatch(string search)
        {
            if (TabHasMatch(Tab.Tweaks, search))
            {
                return Tab.Tweaks;
            }

            if (TabHasMatch(Tab.Config, search))
            {
                return Tab.Config;
            }

            return _tab;
        }

        private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            BuildSections();
        }

        // ---------------------------------------------------------------- running

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            WinUtilSelection selection = BuildSelection(false);
            if (selection.IsEmpty)
            {
                StatusText.Text = "Pick at least one change first.";
                return;
            }

            if (!Confirm(selection, false))
            {
                StatusText.Text = "Cancelled. Nothing was changed.";
                return;
            }

            await RunAsync(selection, "Applying the changes...");
        }

        private async void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            WinUtilSelection selection = BuildUndoSelection();
            if (selection.IsEmpty)
            {
                StatusText.Text = "Pick the changes that should be put back first.";
                return;
            }

            if (!Confirm(selection, true))
            {
                StatusText.Text = "Cancelled. Nothing was changed.";
                return;
            }

            await RunAsync(selection, "Putting the changes back...");
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            Button button = sender as Button;
            string key = button == null ? null : button.Tag as string;
            WinUtilEntry entry = _catalog.FindEntry(key);
            if (entry == null)
            {
                return;
            }

            // A button that only opens a window of Windows is started at once. It needs no
            // administrator rights, so the user is not asked for them.
            bool opened;
            string openError;
            if (WinUtilActions.TryOpenPanel(entry, out opened, out openError))
            {
                string text = opened ? entry.ButtonText + " was opened." : openError;
                AddLog(new WinUtilLogLine(text, opened ? WinUtilLogLevel.Success : WinUtilLogLevel.Error));
                StatusText.Text = text;
                return;
            }

            WinUtilSelection selection = new WinUtilSelection();
            selection.Actions.Add(new WinUtilAction { Key = entry.Key, Undo = false });

            if (!Confirm(selection, false))
            {
                StatusText.Text = "Cancelled. Nothing was changed.";
                return;
            }

            await RunAsync(selection, "Running " + entry.Content + "...");
        }

        private WinUtilSelection BuildSelection(bool undo)
        {
            WinUtilSelection selection = new WinUtilSelection();
            HashSet<string> added = new HashSet<string>(StringComparer.Ordinal);

            if (undo)
            {
                return selection;
            }

            foreach (string key in _selected)
            {
                WinUtilAppxEntry app = _catalog.FindApp(key);
                if (app != null)
                {
                    selection.AppxKeys.Add(key);
                    continue;
                }

                WinUtilEntry entry = _catalog.FindEntry(key);
                if (entry == null)
                {
                    continue;
                }

                string choice;
                _comboChoices.TryGetValue(key, out choice);

                selection.Actions.Add(new WinUtilAction
                {
                    Key = key,
                    Undo = false,
                    Choice = choice ?? string.Empty
                });

                added.Add(key);
            }

            foreach (KeyValuePair<string, bool> item in _toggleInitial)
            {
                bool desired;
                if (!_toggleDesired.TryGetValue(item.Key, out desired) || desired == item.Value)
                {
                    continue;
                }

                if (!added.Add(item.Key))
                {
                    continue;
                }

                selection.Actions.Add(new WinUtilAction
                {
                    Key = item.Key,
                    Undo = !desired
                });
            }

            foreach (KeyValuePair<string, string> item in _comboChoices)
            {
                string initial;
                if (_comboInitial.TryGetValue(item.Key, out initial) &&
                    string.Equals(initial, item.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!added.Add(item.Key))
                {
                    continue;
                }

                selection.Actions.Add(new WinUtilAction
                {
                    Key = item.Key,
                    Undo = false,
                    Choice = item.Value
                });
            }

            if (!string.IsNullOrEmpty(_dnsChoice) &&
                !string.Equals(_dnsChoice, "Default", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_dnsChoice, _dnsInitial, StringComparison.Ordinal))
            {
                selection.DnsProvider = _dnsChoice;
            }

            if (HasPowerPlanChoice())
            {
                selection.Actions.Add(new WinUtilAction { Key = _powerPlan, Undo = false });
            }

            return selection;
        }

        private WinUtilSelection BuildUndoSelection()
        {
            WinUtilSelection selection = new WinUtilSelection();

            foreach (string key in _selected)
            {
                WinUtilEntry entry = _catalog.FindTweak(key);
                if (entry == null || entry.IsFeature)
                {
                    continue;
                }

                selection.Actions.Add(new WinUtilAction { Key = key, Undo = true });
            }

            return selection;
        }

        private bool Confirm(WinUtilSelection selection, bool undo)
        {
            List<string> lines = new List<string>();

            foreach (WinUtilAction action in selection.Actions)
            {
                WinUtilEntry entry = _catalog.FindEntry(action.Key);
                if (entry == null)
                {
                    continue;
                }

                lines.Add("- " + (undo ? "Undo: " : string.Empty) + entry.Content);
            }

            foreach (string key in selection.AppxKeys)
            {
                WinUtilAppxEntry app = _catalog.FindApp(key);
                if (app != null)
                {
                    lines.Add("- Remove the application " + app.Content);
                }
            }

            if (!string.IsNullOrEmpty(selection.DnsProvider))
            {
                lines.Add("- Set the name servers to " + selection.DnsProvider);
            }

            string message = (undo
                ? "The following changes are put back the way the definitions name:"
                : "The following changes are made on this computer:") +
                Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, lines.ToArray()) +
                Environment.NewLine + Environment.NewLine +
                "Some changes need a restart of Windows before they are visible.";

            return ConfirmDialog.Show(
                Window.GetWindow(this),
                (undo ? "Undo " : "Apply ") + lines.Count + " change(s)?",
                message,
                "The local WinUtil engine (registry, services, DISM and bundled local scripts).",
                "the definitions inside this program",
                "Administrator rights are needed, so Windows will show a prompt. A hidden elevated instance of this program does the work; no other window is opened.");
        }

        private async System.Threading.Tasks.Task RunAsync(WinUtilSelection selection, string status)
        {
            SetBusy(true, status);
            AddLog(new WinUtilLogLine(status, WinUtilLogLevel.Info));

            WinUtilRunResult result = await System.Threading.Tasks.Task.Run(
                delegate { return WinUtilRunner.Run(selection, Report); });

            AddLog(new WinUtilLogLine(result.Message, result.Success ? WinUtilLogLevel.Success : WinUtilLogLevel.Error));

            if (result.Success)
            {
                _selected.Clear();
                _toggleDesired.Clear();
                _comboChoices.Clear();
                _dnsInitial = _dnsChoice;
                _powerPlan = string.Empty;
            }

            // Read the registry again, so the toggles show the state that Windows has now.
            double offset = ContentScroll.VerticalOffset;
            BuildSections();
            ContentScroll.ScrollToVerticalOffset(offset);

            SetBusy(false, result.Message);
            UpdateSelectionCount();
            LogService.Info("WinUtil: " + result.Message);
        }

        private void SetBusy(bool busy, string message)
        {
            _busy = busy;

            RunProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ApplyButton.IsEnabled = !busy;
            UndoButton.IsEnabled = !busy;
            SearchBox.IsEnabled = !busy;
            CategoryBox.IsEnabled = !busy && _tab != Tab.Updates;
            TabTweaks.IsEnabled = !busy;
            TabConfig.IsEnabled = !busy;
            TabUpdates.IsEnabled = !busy;
            PresetBar.IsEnabled = !busy;

            if (!string.IsNullOrEmpty(message))
            {
                StatusText.Text = message;
                LogStatus.Text = message;
            }

            if (!busy)
            {
                UpdateSelectionCount();
            }
        }

        private void Report(WinUtilLogLine line)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(delegate { AddLog(line); }));
        }

        private void AddLog(WinUtilLogLine line)
        {
            if (line == null)
            {
                return;
            }

            string brushKey;
            switch (line.Level)
            {
                case WinUtilLogLevel.Success:
                    brushKey = "SuccessBrush";
                    break;
                case WinUtilLogLevel.Warning:
                    brushKey = "WarningBrush";
                    break;
                case WinUtilLogLevel.Error:
                    brushKey = "DangerBrush";
                    break;
                default:
                    brushKey = "TextSecondaryBrush";
                    break;
            }

            TextBlock text = new TextBlock
            {
                Text = DateTime.Now.ToString("HH:mm:ss") + "  " + line.Text,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas, Cascadia Mono"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 1)
            };

            text.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            LogPanel.Children.Add(text);
            _logLines++;

            while (_logLines > 500 && LogPanel.Children.Count > 0)
            {
                LogPanel.Children.RemoveAt(0);
                _logLines--;
            }

            LogScroll.ScrollToEnd();
            LogStatus.Text = line.Text;
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogPanel.Children.Clear();
            _logLines = 0;
            LogStatus.Text = "The log is cleared.";
        }

        private void LogToggle_Click(object sender, RoutedEventArgs e)
        {
            bool show = LogScroll.Visibility != Visibility.Visible;
            LogScroll.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            // The arrow points the way the log moves: down hides it, up shows it again.
            LogToggle.Content = show ? "\uE70D" : "\uE70E";
            string text = show ? "Hide the activity log" : "Show the activity log";
            LogToggle.ToolTip = text;
            AutomationProperties.SetName(LogToggle, text);
        }
    }
}
