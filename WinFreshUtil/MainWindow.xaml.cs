using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WinFreshUtil.Models;
using WinFreshUtil.Services;
using WinFreshUtil.Theme;

namespace WinFreshUtil
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Pages that have already been created, keyed by navigation key.
        /// </summary>
        private readonly Dictionary<string, UserControl> _pages =
            new Dictionary<string, UserControl>(StringComparer.Ordinal);

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            VersionText.Text = "Version " + AppInfo.Version;
            UpdateThemeButton();

            if (NavDrivers.IsChecked != true)
            {
                NavDrivers.IsChecked = true;
            }
            else
            {
                Show(PageFactory.Drivers);
            }
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            RadioButton item = sender as RadioButton;
            if (item == null || ContentHost == null)
            {
                return;
            }

            if (item == NavDrivers)
            {
                Show(PageFactory.Drivers);
            }
            else if (item == NavWinUtil)
            {
                Show(PageFactory.WinUtil);
            }
            else if (item == NavApps)
            {
                Show(PageFactory.Apps);
            }
            else if (item == NavWindowsOffice)
            {
                Show(PageFactory.WindowsOffice);
            }
            else if (item == NavAbout)
            {
                Show(PageFactory.About);
            }
        }

        private void Show(string key)
        {
            UserControl page;
            if (!_pages.TryGetValue(key, out page))
            {
                page = PageFactory.Create(key);
                if (page == null)
                {
                    return;
                }

                _pages[key] = page;
            }

            ContentHost.Content = page;
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Toggle();
            UpdateThemeButton();
        }

        private void UpdateThemeButton()
        {
            // The symbol shows the theme that is on, and the tooltip names the theme that
            // the button switches to. A sun is the light theme, a moon the dark one.
            string glyph = ThemeManager.IsDark ? "\uE708" : "\uE706";
            string next = ThemeManager.IsDark ? "light" : "dark";

            ThemeButton.Content = glyph;
            ThemeButton.ToolTip = "Switch to the " + next + " theme";
            System.Windows.Automation.AutomationProperties.SetName(
                ThemeButton, "Switch to the " + next + " theme");

            ThemeName.Text = ThemeManager.IsDark ? "Dark theme" : "Light theme";
        }
    }
}
