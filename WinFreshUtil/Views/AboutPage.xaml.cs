using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WinFreshUtil.Models;
using WinFreshUtil.Services;

namespace WinFreshUtil.Views
{
    public partial class AboutPage : UserControl
    {
        public AboutPage()
        {
            InitializeComponent();
            LogPathText.Text = LogService.GetLogPath();
        }

        private void Link_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }

            string url = button.Tag as string;
            string error;
            if (!UrlService.TryOpen(url, out error))
            {
                MessageBox.Show(
                    "The link could not be opened.\r\n\r\n" + error,
                    "WinFreshUtil",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OpenLog_Click(object sender, RoutedEventArgs e)
        {
            string path = LogService.GetLogPath();

            try
            {
                if (!File.Exists(path))
                {
                    MessageBox.Show(
                        "There is no log file yet.\r\n\r\nIt is written to:\r\n" + path,
                        "WinFreshUtil",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogService.Error("The log file could not be opened.", ex);
                MessageBox.Show(
                    "The log file could not be opened.\r\n\r\n" + ex.Message,
                    "WinFreshUtil",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            string folder = AppPaths.GetDataFolder();

            try
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogService.Error("The data folder could not be opened.", ex);
                MessageBox.Show(
                    "The folder could not be opened.\r\n\r\n" + ex.Message,
                    "WinFreshUtil",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
