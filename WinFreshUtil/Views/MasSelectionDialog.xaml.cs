using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WinFreshUtil.Models;

namespace WinFreshUtil.Views
{
    /// <summary>
    /// Lets the user pick one of the options of a MAS function before it is run.
    /// </summary>
    public partial class MasSelectionDialog : Window
    {
        private MasOption _selected;

        public MasSelectionDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows the dialog and returns the option the user picked, or null when the
        /// dialog was cancelled.
        /// </summary>
        public static MasOption Show(Window owner, string title, IList<MasOption> options)
        {
            MasSelectionDialog dialog = new MasSelectionDialog();

            dialog.TitleText.Text = title;
            dialog.DescriptionText.Text = "Pick the function that should be run.";
            dialog.OptionList.ItemsSource = options;

            if (options != null && options.Count > 0)
            {
                dialog.OptionList.SelectedIndex = 0;
                dialog._selected = options[0];
            }

            dialog.RunButton.IsEnabled = options != null && options.Count > 0;

            if (owner != null && owner.IsLoaded)
            {
                dialog.Owner = owner;
            }

            return dialog.ShowDialog() == true ? dialog._selected : null;
        }

        private void OptionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selected = OptionList.SelectedItem as MasOption;
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
            {
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
