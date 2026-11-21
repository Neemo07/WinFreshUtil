using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WinFreshUtil.Services;

namespace WinFreshUtil.Theme
{
    /// <summary>
    /// Switches the colour palette and asks the window manager for a dark title bar.
    /// The colours live in Theme.xaml; this class only changes their values, so every
    /// DynamicResource follows without rebuilding a view.
    /// </summary>
    public static class ThemeManager
    {
        private static bool _initialized;

        public static bool IsDark { get; private set; }

        /// <summary>
        /// Raised after the colours were replaced, so a page that builds its own elements
        /// can build them again with the new colours.
        /// </summary>
        public static event EventHandler Changed;

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            Flatten();
            Apply(LoadPreference());
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));
        }

        public static void Toggle()
        {
            Apply(!IsDark);
            SavePreference(IsDark);

            if (Application.Current != null)
            {
                foreach (Window window in Application.Current.Windows)
                {
                    ApplyWindow(window);
                }
            }
        }

        public static void Apply(bool dark)
        {
            IsDark = dark;

            SetBrush("AppBackgroundBrush", dark ? "#0E1116" : "#F5F7FA");
            SetBrush("SidebarBrush", dark ? "#12161D" : "#EEF1F5");
            SetBrush("SurfaceBrush", dark ? "#171C24" : "#FFFFFF");
            SetBrush("SurfaceAltBrush", dark ? "#1C222C" : "#F7F9FC");
            SetBrush("SurfaceHoverBrush", dark ? "#232B37" : "#EDF2F9");
            SetBrush("InputBrush", dark ? "#10151C" : "#FFFFFF");
            SetBrush("BorderBrush", dark ? "#2A3341" : "#DDE3EC");
            SetBrush("BorderStrongBrush", dark ? "#3A4658" : "#C6CFDC");
            SetBrush("TextPrimaryBrush", dark ? "#E9EEF6" : "#16202E");
            SetBrush("TextSecondaryBrush", dark ? "#A7B1C2" : "#4B586B");
            SetBrush("TextMutedBrush", dark ? "#7C8798" : "#6F7C8F");
            SetBrush("AccentBrush", dark ? "#4C8DFF" : "#2563EB");
            SetBrush("AccentHoverBrush", dark ? "#6BA1FF" : "#3B82F6");
            SetBrush("AccentPressedBrush", dark ? "#3B78E0" : "#1D4ED8");
            SetBrush("AccentTextBrush", "#FFFFFF");
            SetBrush("SelectionBrush", dark ? "#263B5E" : "#DCE7FB");
            SetBrush("SuccessBrush", dark ? "#3FB950" : "#1A7F37");
            SetBrush("WarningBrush", dark ? "#D29922" : "#9A6700");
            SetBrush("DangerBrush", dark ? "#F85149" : "#CF222E");
            SetBrush("OverlayBrush", dark ? "#0B0E13" : "#E8EDF5");
            SetBrush("ScrollThumbBrush", dark ? "#3A4658" : "#C6CFDC");

            EventHandler changed = Changed;
            if (changed != null)
            {
                changed(null, EventArgs.Empty);
            }
        }

        public static void ApplyWindow(Window window)
        {
            if (window == null)
            {
                return;
            }

            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int value = IsDark ? 1 : 0;
            int result = DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));

            if (result != 0)
            {
                DwmSetWindowAttribute(handle, 19, ref value, sizeof(int));
            }

            // Rounded corners on Windows 11; older versions ignore the request.
            int preference = 2;
            DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            Window window = sender as Window;
            if (window != null)
            {
                ApplyWindow(window);
            }
        }

        private static void SetBrush(string key, string color)
        {
            ResourceDictionary owner = FindPalette();
            if (owner == null)
            {
                return;
            }

            SolidColorBrush brush = FindBrush(owner, key);
            if (brush == null)
            {
                return;
            }

            Color parsed = (Color)ColorConverter.ConvertFromString(color);

            // The entry is replaced instead of the colour of the brush. A DynamicResource
            // follows a replaced entry; a brush that is changed in place is not reported
            // to the elements that use it.
            owner[key] = new SolidColorBrush(parsed);
        }

        /// <summary>
        /// Copies the entries of the theme file into the resources of the application.
        /// A DynamicResource follows an entry that is replaced in the resources of the
        /// application; an entry that is replaced inside a merged file is not reported.
        /// </summary>
        private static void Flatten()
        {
            if (Application.Current == null)
            {
                return;
            }

            ResourceDictionary resources = Application.Current.Resources;

            foreach (ResourceDictionary merged in resources.MergedDictionaries)
            {
                List<DictionaryEntry> entries = new List<DictionaryEntry>();

                foreach (DictionaryEntry entry in merged)
                {
                    entries.Add(entry);
                }

                foreach (DictionaryEntry entry in entries)
                {
                    resources[entry.Key] = entry.Value;
                }
            }
        }

        /// <summary>
        /// The dictionary that holds the colours, which is the one file that Theme.xaml
        /// was merged as.
        /// </summary>
        private static ResourceDictionary FindPalette()
        {
            if (Application.Current == null)
            {
                return null;
            }

            ResourceDictionary resources = Application.Current.Resources;
            if (FindBrush(resources, "SurfaceBrush") != null)
            {
                return resources;
            }

            foreach (ResourceDictionary merged in resources.MergedDictionaries)
            {
                if (FindBrush(merged, "SurfaceBrush") != null)
                {
                    return merged;
                }
            }

            return null;
        }

        /// <summary>
        /// The brush of one key in one dictionary, or null. The search does not look into
        /// the dictionaries that this one merged, so the owner of the value is found.
        /// </summary>
        private static SolidColorBrush FindBrush(ResourceDictionary dictionary, string key)
        {
            if (dictionary == null)
            {
                return null;
            }

            foreach (DictionaryEntry entry in dictionary)
            {
                if (string.Equals(entry.Key as string, key, StringComparison.Ordinal))
                {
                    return entry.Value as SolidColorBrush;
                }
            }

            return null;
        }

        private static bool LoadPreference()
        {
            try
            {
                string path = GetPreferencePath();
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path).Trim();
                    if (string.Equals(text, "light", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (string.Equals(text, "dark", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // The system setting is used when the file cannot be read.
            }

            return SystemUsesDarkTheme();
        }

        private static void SavePreference(bool dark)
        {
            try
            {
                string path = GetPreferencePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, dark ? "dark" : "light");
            }
            catch (Exception)
            {
                // The choice lasts for this run when it cannot be written.
            }
        }

        private static string GetPreferencePath()
        {
            return Path.Combine(AppPaths.GetDataFolder(), "theme.txt");
        }

        private static bool SystemUsesDarkTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("AppsUseLightTheme");
                        if (value != null)
                        {
                            return Convert.ToInt32(value) == 0;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Dark is the design default.
            }

            return true;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
