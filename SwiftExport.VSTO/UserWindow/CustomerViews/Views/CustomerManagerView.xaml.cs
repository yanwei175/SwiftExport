using SwiftExport.VSTO.UserWindow.CustomerViews.ViewModels;
using System.Windows;

namespace SwiftExport.VSTO.UserWindow.CustomerViews.Views
{
    /// <summary>
    /// MyWindow.xaml 的交互逻辑
    /// </summary>
    public partial class CustomerManagerView : Window
    {
        public CustomerManagerView(CustomerManagerViewModel vm)
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
