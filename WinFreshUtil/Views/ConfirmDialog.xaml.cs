using System.Windows;

namespace WinFreshUtil.Views
{
    /// <summary>
    /// Confirmation dialog used before anything external is downloaded or executed.
    /// It always states exactly what will run and where it comes from.
    /// </summary>
    public partial class ConfirmDialog : Window
    {
        public ConfirmDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows the dialog and returns true when the user confirmed.
        /// </summary>
        public static bool Show(
            Window owner,
            string title,
            string message,
            string command,
            string source,
            string warning,
            string commandLabel = null,
            string confirmText = null)
        {
            ConfirmDialog dialog = new ConfirmDialog();

            dialog.TitleText.Text = title ?? string.Empty;
            dialog.MessageText.Text = message ?? string.Empty;
            dialog.CommandText.Text = command ?? string.Empty;
            dialog.SourceText.Text = source ?? string.Empty;

            if (!string.IsNullOrEmpty(commandLabel))
            {
                dialog.CommandLabel.Text = commandLabel;
            }

            if (!string.IsNullOrEmpty(confirmText))
            {
                dialog.ConfirmButton.Content = confirmText;
            }

            if (string.IsNullOrEmpty(warning))
            {
                dialog.WarningText.Visibility = Visibility.Collapsed;
            }
            else
            {
                dialog.WarningText.Visibility = Visibility.Visible;
                dialog.WarningText.Text = warning;
            }

            if (owner != null && owner.IsLoaded)
            {
                dialog.Owner = owner;
            }

            return dialog.ShowDialog() == true;
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
