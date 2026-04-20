using SwiftExport.View.FW.ProductViews.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.ProductViews.Views
{
    /// <summary>
    /// ProductColorManagerView.xaml 的交互逻辑
    /// </summary>
    public partial class ProductColorManagerView : UserControl
    {
        public ProductColorManagerView()
        {
            InitializeComponent();
        }

        public ProductColorManagerView(ProductColorManagerViewModel vm) : this()
        {
            this.DataContext = vm;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            // Close logic ignored for UserControl test
        }
    }
}










