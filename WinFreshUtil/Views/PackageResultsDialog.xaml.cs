using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WinFreshUtil.Models;

namespace WinFreshUtil.Views
{
    /// <summary>
    /// Shows the programs that the package manager of Windows knows, and lets the
    /// user pick the one that is installed.
    /// </summary>
    public partial class PackageResultsDialog : Window
    {
        public PackageResultsDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows the dialog and returns the program the user picked, or null.
        /// </summary>
        public static AppPackage Show(
            Window owner,
            string query,
            IList<AppPackage> results)
        {
            PackageResultsDialog dialog = new PackageResultsDialog();

            dialog.TitleText.Text = "The programs that the package manager knows for \"" + query + "\"";
            dialog.MessageText.Text = results.Count + " program(s) were found. The program is installed " +
                                      "with the package manager of Windows.";

            foreach (AppPackage package in results)
            {
                dialog.ResultList.Items.Add(package);
            }

            if (results.Count > 0)
            {
                dialog.ResultList.SelectedIndex = 0;
            }
            else
            {
                dialog.InstallButton.IsEnabled = false;
            }

            if (owner != null && owner.IsLoaded)
            {
                dialog.Owner = owner;
            }

            return dialog.ShowDialog() == true ? dialog.Selected : null;
        }

        private AppPackage Selected { get; set; }

        private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Selected = ResultList.SelectedItem as AppPackage;
            InstallButton.IsEnabled = Selected != null;
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
