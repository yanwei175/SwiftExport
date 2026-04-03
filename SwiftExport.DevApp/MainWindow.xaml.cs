using SwiftExport.View.FW.CustomerViews.Views;
using SwiftExport.View.FW.DictViews.Views;
using SwiftExport.View.FW.ProductViews.Views;
using SwiftExport.View.FW.SupplierViews.Views;
using System.Windows;

namespace SwiftExport.DevApp
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var win = DiServices.GetService<DictManagerView>();
            win.ShowDialog();
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            var win = DiServices.GetService<SupplierManagerView>();
            win.ShowDialog();
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            var win = DiServices.GetService<CustomerPoConfigManagerView>();
            win.ShowDialog();
        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            var win = DiServices.GetService<ProductCategoryManagerView>();
            win.ShowDialog();
        }

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {
            var win = DiServices.GetService<ProductManagerView>();

            var newwin = new Window
            {
                Title = "产品与BOM管理中心 - 独立测试环境",
                Width = 1920,  // 稍微给大一点，主从表挤在小窗口不好看
                Height = 1080,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, // 启动时屏幕居中
                
                // ★ 核心修复：直接把 UserControl 塞给 Window，它天生就会 100% 完美撑满！绝对不需要 DockPanel！
                Content = win
                
            };

            newwin.ShowDialog();
        }

        private void Button_Click_5(object sender, RoutedEventArgs e)
        {
            var win = new TestWindow();
            win.ShowDialog();
        }
    }
}
