using SwiftExport.View.FW.PackingListViews.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.PackingListViews.Views
{
    public partial class PackingListManagerView : UserControl
    {
        public PackingListManagerView()
        {
            InitializeComponent();
        }
        public PackingListManagerView(PackingListManagerViewModel vm) : this()
        {
            this.DataContext = vm;
        }

        private void FilterPopup_Opened(object sender, EventArgs e)
        {
            FilterPopupTextBox.Focus();
            FilterPopupTextBox.CaretIndex = FilterPopupTextBox.Text.Length;

        }

        private void FilterPopupTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            FilterPopup.IsOpen = false;
        }

        private void FilterTextBoxSmall_GotFocus(object sender, RoutedEventArgs e)
        {
            FilterPopup.IsOpen = true;

        }

    }
}
