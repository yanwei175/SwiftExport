using SwiftExport.View.FW.Independent.ViewModels;
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

namespace SwiftExport.View.FW.Independent.Views
{
    /// <summary>
    /// IndependentDemandView.xaml 的交互逻辑
    /// </summary>
    public partial class IndependentDemandManagerView : UserControl
    {
        public IndependentDemandManagerView(IndependentDemandManagerViewModel vm)
        {
            this.DataContext = vm;
            InitializeComponent();
        }

        private void FilterTextBoxSmall_GotFocus(object sender, RoutedEventArgs e)
        {
            FilterPopup.IsOpen = true;
        }

        private void FilterPopupTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            FilterPopup.IsOpen = false;
        }

        private void FilterPopup_Opened(object sender, EventArgs e)
        {
            FilterPopupTextBox.Focus();
            FilterPopupTextBox.CaretIndex = FilterPopupTextBox.Text.Length;
        }
    }
}
