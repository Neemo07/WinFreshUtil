using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WinFreshUtil.Services;
using WinFreshUtil.Services.Driver;

namespace WinFreshUtil.Views
{
    /// <summary>
    /// Shows the drivers that were found for a device and lets the user pick the one
    /// that is installed. The dialog always states the name, the version and the place
    /// the driver comes from.
    /// </summary>
    public partial class DriverChoiceDialog : Window
    {
        public DriverChoiceDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows the dialog and returns the driver the user picked, or null when the
        /// user did not want to install anything.
        /// </summary>
        public static DriverCandidate Show(
            Window owner,
            string title,
            string message,
            IList<DriverCandidate> candidates,
            string warning)
        {
            DriverChoiceDialog dialog = new DriverChoiceDialog();

            dialog.TitleText.Text = title ?? string.Empty;
            dialog.MessageText.Text = message ?? string.Empty;

            if (string.IsNullOrEmpty(warning))
            {
                dialog.WarningText.Visibility = Visibility.Collapsed;
            }
            else
            {
                dialog.WarningText.Text = warning;
            }

            if (candidates != null && candidates.Count == 1)
            {
                dialog.ListTitle.Text = "The driver that was found";
            }
            else
            {
                dialog.ListTitle.Text = "The drivers that were found, pick the one that is wanted";
            }

            if (candidates != null)
            {
                foreach (DriverCandidate candidate in candidates)
                {
                    dialog.CandidateList.Items.Add(candidate);
                }
            }

            if (candidates != null && candidates.Count > 0)
            {
                dialog.CandidateList.SelectedIndex = 0;
            }

            if (owner != null && owner.IsLoaded)
            {
                dialog.Owner = owner;
            }

            return dialog.ShowDialog() == true ? dialog.Selected : null;
        }

        private DriverCandidate Selected { get; set; }

        private void CandidateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DriverCandidate candidate = CandidateList.SelectedItem as DriverCandidate;
            Selected = candidate;

            if (candidate == null)
            {
                DriverText.Text = string.Empty;
                CommandText.Text = string.Empty;
                SourceText.Text = string.Empty;
                ConfirmButton.IsEnabled = false;
                return;
            }

            ConfirmButton.IsEnabled = true;

            string text = candidate.Name;

            if (!string.IsNullOrEmpty(candidate.Version))
            {
                text += "\r\nVersion: " + candidate.Version;
            }

            if (!string.IsNullOrEmpty(candidate.Released))
            {
                text += "\r\nMade: " + candidate.Released;
            }

            if (!string.IsNullOrEmpty(candidate.HardwareId))
            {
                text += "\r\nHardware id: " + candidate.HardwareId;
            }

            if (candidate.Size > 0)
            {
                text += "\r\nSize: " + (candidate.Size / 1024 / 1024) + " MB";
            }

            DriverText.Text = text;
            CommandText.Text = DriverInstaller.GetInstallCommand(candidate);
            SourceText.Text = candidate.SourceUrl ?? string.Empty;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
