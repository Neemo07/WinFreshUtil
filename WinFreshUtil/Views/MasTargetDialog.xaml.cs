using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WinFreshUtil.Models;

namespace WinFreshUtil.Views
{
    /// <summary>
    /// Lets the user pick the target of a function, for example the edition of Windows
    /// or the package of Office that should be installed.
    /// </summary>
    public partial class MasTargetDialog : Window
    {
        private MasTarget _selected;

        public MasTargetDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows the dialog and returns the target the user picked, or null when the
        /// dialog was cancelled.
        /// </summary>
        public static MasTarget Show(Window owner, string title, IList<MasTarget> targets)
        {
            MasTargetDialog dialog = new MasTargetDialog();

            dialog.TitleText.Text = title;
            dialog.DescriptionText.Text = "Choose what should be changed.";
            dialog.TargetList.ItemsSource = targets;

            if (targets != null && targets.Count > 0)
            {
                dialog.TargetList.SelectedIndex = 0;
                dialog._selected = targets[0];
            }

            dialog.RunButton.IsEnabled = targets != null && targets.Count > 0;

            if (owner != null && owner.IsLoaded)
            {
                dialog.Owner = owner;
            }

            return dialog.ShowDialog() == true ? dialog._selected : null;
        }

        private void TargetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selected = TargetList.SelectedItem as MasTarget;
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
