using SwiftExport.View.FW.SupplierViews.ViewModels;
using System.Windows;

namespace SwiftExport.View.FW.SupplierViews.Views
{
    /// <summary>
    /// MyWindow.xaml 的交互逻辑
    /// </summary>
    public partial class SupplierManagerView : Window
    {
        public SupplierManagerView(SupplierManagerViewModel vm)
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
