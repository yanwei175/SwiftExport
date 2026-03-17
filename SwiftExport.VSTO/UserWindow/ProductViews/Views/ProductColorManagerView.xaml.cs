using SwiftExport.VSTO.UserWindow.ProductViews.ViewModels;
using System.Windows;

namespace SwiftExport.VSTO.UserWindow.ProductViews.Views
{
    /// <summary>
    /// MyWindow.xaml 的交互逻辑
    /// </summary>
    public partial class ProductColorManagerView : Window
    {
        public ProductColorManagerView(ProductColorManagerViewModel vm)
        {
            InitializeComponent();
            this.DataContext = vm;
        }


        // 关闭窗体
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
