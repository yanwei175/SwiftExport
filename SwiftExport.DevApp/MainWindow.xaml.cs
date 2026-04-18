using SwiftExport.View.FW.CustomerViews.Views;
using SwiftExport.View.FW.DictViews.Views;
using SwiftExport.View.FW.ProductViews.Views;
using SwiftExport.View.FW.SalesOrderViews.Views;
using SwiftExport.View.FW.SalesOrderViews.ViewModels;
using SwiftExport.View.FW.SupplierViews.Views;
using System.Windows;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.View.FW.CustomerViews.ViewModels;

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
                Height = 920,
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

        /// <summary>
        /// 中文注释：销售订单管理 - 主表列表测试
        /// </summary>
        private void Button_Click_6(object sender, RoutedEventArgs e)
        {
            var vm = DiServices.GetService<SalesOrderManagerViewModel>();
            var view = DiServices.GetService<SalesOrderManagerView>();
            view.DataContext = vm;

            var window = new Window
            {
                Title = "Sales Order Management - Test Environment",
                Width = 1920,
                Height = 920,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = view
            };

            window.ShowDialog();
        }

        /// <summary>
        /// 中文注释：销售订单详情 - 新增订单测试（orderId = 0）
        /// </summary>
        private void Button_Click_7(object sender, RoutedEventArgs e)
        {
            var vm = new SalesOrderDetailViewModel(0, DiServices.GetService<SwiftExport.Contracts.Apis.SalesOrderApis.ISalesOrderApi>(), 
                                                   DiServices.GetService<SwiftExport.Contracts.UiInterface.IDialogService>());
            var window = DiServices.GetService<SalesOrderDetailView>();
            window.DataContext = vm;
            window.ShowDialog();
        }

        /// <summary>
        /// 中文注释：销售订单详情 - 编辑订单测试（orderId = 1）
        /// </summary>
        private void Button_Click_8(object sender, RoutedEventArgs e)
        {
            var vm = new SalesOrderDetailViewModel(1, DiServices.GetService<SwiftExport.Contracts.Apis.SalesOrderApis.ISalesOrderApi>(), 
                                                   DiServices.GetService<SwiftExport.Contracts.UiInterface.IDialogService>());
            var window = DiServices.GetService<SalesOrderDetailView>();
            window.DataContext = vm;
            window.ShowDialog();
        }

        private void Button_Click_9(object sender, RoutedEventArgs e)
        {
            var vm = DiServices.GetService<CustomerManagerViewModel>();
            var view = DiServices.GetService<CustomerManagerView>();
            view.DataContext = vm;

            var window = new Window
            {
                Title = "Sales Order Management - Test Environment",
                Width = 1920,
                Height = 920,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = view
            };

            window.ShowDialog();
        }
    }
}
