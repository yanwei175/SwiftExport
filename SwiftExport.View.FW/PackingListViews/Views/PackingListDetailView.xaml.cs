using SwiftExport.View.FW.PackingListViews.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SwiftExport.View.FW.PackingListViews.Views
{
    /// <summary>
    /// PackingListDetailView.xaml 的交互逻辑
    /// </summary>
    public partial class PackingListDetailView : Window
    {
        public PackingListDetailView()
        {
            InitializeComponent();
        }
        public PackingListDetailView(PackingListDetailViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }
        private void IncotermSortRemarkPopupTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            IncotermSortRemarkPopup.IsOpen = false;
        }

        private void IncotermSortRemarkPopup_Opened(object sender, EventArgs e)
        {
            IncotermSortRemarkPopupTextBox.Focus();
            IncotermSortRemarkPopupTextBox.CaretIndex = IncotermSortRemarkPopupTextBox.Text.Length;

        }

        private void IncotermSortRemarkTextBoxSmall_GotFocus(object sender, RoutedEventArgs e)
        {
            IncotermSortRemarkPopup.IsOpen = true;
        }
    }
}
