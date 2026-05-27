using SwiftExport.DevApp.ViewModels;
using SwiftExport.View.FW.CustomerViews.Views;
using SwiftExport.View.FW.DictViews.Views;
using SwiftExport.View.FW.DictViews.ViewModels;
using SwiftExport.View.FW.Http;
using SwiftExport.View.FW.OrderDecomposeViews.Views;
using SwiftExport.View.FW.ProductViews.Views;
using SwiftExport.View.FW.PurchaseOrderViews.Views;
using SwiftExport.View.FW.SalesOrderViews.Views;
using SwiftExport.View.FW.SystemViews.ViewModels;
using SwiftExport.View.FW.SystemViews.Views;
using SwiftExport.View.FW.SupplierViews.Views;
using System.Windows;
using SwiftExport.View.FW.WarehouseViews.Views;
using SwiftExport.View.FW.PackingListViews.Views;

namespace SwiftExport.DevApp
{
    /// <summary>
    /// 主窗口 Shell。
    /// 顶栏绑定 ShellViewModel 显示用户信息与退出/切换命令。
    /// 启动时若未登录，强制弹出登录窗口（主窗口保持隐藏直到登录成功）。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ShellViewModel _shellVm;

        public MainWindow(ShellViewModel shellVm)
        {
            InitializeComponent();

            _shellVm = shellVm;
            DataContext = _shellVm;

            // ShellViewModel 请求显示登录窗口（退出 / 切换用户后触发）
            _shellVm.ShowLoginRequested += () => ShowLoginWindow();

            // 窗口加载完成后，若未登录则强制弹出登录框
            Loaded += OnMainWindowLoaded;
        }

        // ──────────────────────────────────────────────────────
        // 启动时强制登录
        // ──────────────────────────────────────────────────────

        private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            //if (!AuthTokenStore.IsLoggedIn)
            //    ShowLoginWindow(title: "请登录以继续");
        }

        // ──────────────────────────────────────────────────────
        // 顶栏"点击登录"按钮（未登录状态显示）
        // ──────────────────────────────────────────────────────

        private void BtnTopLogin_Click(object sender, RoutedEventArgs e)
        {
            ShowLoginWindow();
        }

        // ──────────────────────────────────────────────────────
        // 登录 / 注册弹窗（内容区按钮）
        // ──────────────────────────────────────────────────────

        /// <summary>内容区"登录"按钮</summary>
        private void Button_Click_13(object sender, RoutedEventArgs e) => ShowLoginWindow();

        /// <summary>内容区"注册"按钮</summary>
        private void Button_Click_14(object sender, RoutedEventArgs e)
        {
            var view = DiServices.GetService<RegisterView>();
            view.ShowDialog();
        }

        // ──────────────────────────────────────────────────────
        // 功能模块按钮
        // ──────────────────────────────────────────────────────

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            OpenFullWindow("字典管理", DiServices.GetService<SysViewRouteManagerView>());
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            OpenFullWindow("供应商", DiServices.GetService<SupplierManagerView>());
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            DiServices.GetService<SwiftExport.View.FW.CustomerViews.Views.CustomerPoConfigManagerView>().ShowDialog();
        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            OpenWindow("产品分类", DiServices.GetService<ProductCategoryManagerView>(), 900, 600);
        }

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {
            OpenFullWindow("产品管理", DiServices.GetService<ProductManagerView>());
        }

        private void Button_Click_5(object sender, RoutedEventArgs e)
        {
            new TestWindow().ShowDialog();
        }

        private void Button_Click_6(object sender, RoutedEventArgs e)
        {
            OpenFullWindow("销售订单", DiServices.GetService<SalesOrderManagerView>());
        }

        private void Button_Click_7(object sender, RoutedEventArgs e)
        {
            OpenFullWindow("销售订单详情", DiServices.GetService<SalesOrderDetailView>());
        }

        private void Button_Click_8(object sender, RoutedEventArgs e)
        {
            OpenFullWindow("数据字典", DiServices.GetService<DataDictManagerView>());
        }

        private void Button_Click_9(object sender, RoutedEventArgs e)
        {
            OpenFullWindow("客户管理", DiServices.GetService<CustomerManagerView>());
        }

        private void Button_Click_10(object sender, RoutedEventArgs e)
        {
            OpenFullWindow("需求池", DiServices.GetService<DependentDemandManagerView>());
        }

        private void Button_Click_11(object sender, RoutedEventArgs e)
        {
            OpenFullWindow("供应商管理", DiServices.GetService<SupplierManagerView>());
        }

        private void Button_Click_12(object sender, RoutedEventArgs e)
        {
            OpenFullWindow("采购管理", DiServices.GetService<PurchaseOrderManagerView>());
        }

        // ──────────────────────────────────────────────────────
        // 公共辅助方法
        // ──────────────────────────────────────────────────────

        /// <summary>
        /// 弹出登录窗口。登录成功后刷新 ShellViewModel 顶栏用户信息。
        /// </summary>
        private void ShowLoginWindow(string title = "登录")
        {
            var view = DiServices.GetService<LoginView>();

            //// 登录成功：刷新顶栏 + 关闭登录窗口
            //if (view.DataContext is LoginViewModel vm)
            //{
            //    vm.LoginSucceeded += () =>
            //    {
            //        _shellVm.RefreshUserInfo();
            //        view?.Close();
            //    };
            //}

            view.ShowDialog();
        }

        /// <summary>全屏（1920×920）弹出子窗口</summary>
        private static void OpenFullWindow(string title, object content)
            => OpenWindow(title, content, 1920, 920);

        /// <summary>指定大小弹出子窗口</summary>
        private static void OpenWindow(string title, object content, double width, double height)
        {
            var win = new Window
            {
                Title = title,
                Width = width,
                Height = height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = content
            };
            win.ShowDialog();
        }

        private void Button_Click_15(object sender, RoutedEventArgs e)
        {
            OpenFullWindow("入库单", DiServices.GetService<ReceiptDetailView>());
        }

        private void Button_Click_16(object sender, RoutedEventArgs e)
        {
            OpenFullWindow("入库管理", DiServices.GetService<ReceiptManagerView>());

        }

        private void Button_Click_17(object sender, RoutedEventArgs e)
        {
            var win = DiServices.GetService<AwatingInboundView>();
            win.ShowDialog();
        }

        private void Button_Click_18(object sender, RoutedEventArgs e)
        {
            OpenFullWindow("箱单管理", DiServices.GetService<PackingListManagerView>());
        }

        private void Button_Click_19(object sender, RoutedEventArgs e)
        {
            var win = DiServices.GetService<PackingListDetailView>();
            win.ShowDialog();
        }

        private void Button_Click_20(object sender, RoutedEventArgs e)
        {
            var win = DiServices.GetService<UnshippedView>();
            win.ShowDialog();

        }

        private void Button_Click_SalesProductSelector(object sender, RoutedEventArgs e)
        {
            var win = DiServices.GetService<SalesProductSeletorView>();
            win.ShowDialog();
        }
    }
}
