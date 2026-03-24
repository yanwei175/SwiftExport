using Microsoft.Office.Tools.Ribbon;
using SwiftExport.VSTO.UserWindow.CustomerViews.Views;
using SwiftExport.VSTO.UserWindow.ProductViews.ViewModels;
using SwiftExport.VSTO.UserWindow.ProductViews.Views;
using SwiftExport.VSTO.UserWindow.SupplierViews.Views;
using System.Linq;
using System.Windows.Interop;
namespace SwiftExport.VSTO
{
    public partial class RibbonSwift
    {
        private void RibbonSwift_Load(object sender, RibbonUIEventArgs e)
        {

        }

        private void BtnTest_Click(object sender, RibbonControlEventArgs e)
        {
            var win = new ThemeTest.MainWindow();
            win.ShowDialog();

        }

        private void Button1_Click(object sender, RibbonControlEventArgs e)
        {
            // 1. 窗口不存在时，新建并挂载
            if (Globals.ThisAddIn._productSelectorWindow == null)
            {
                var vm = DiServices.GetService<ProductSelectorViewModel>();
                Globals.ThisAddIn._productSelectorWindow = new ProductSelectorWindow(vm);

                var helper = new WindowInteropHelper(Globals.ThisAddIn._productSelectorWindow)
                {
                    Owner = new System.IntPtr(Globals.ThisAddIn.Application.Hwnd)
                };

                Globals.ThisAddIn._productSelectorWindow.Closed += (s, args) =>
                {
                    Globals.ThisAddIn._productSelectorWindow = null;
                };
            }
            // ★ 2. 核心命脉：把当前的 Window 和 ViewModel 用局部变量死死“抓在手里”！
            var currentWindow = Globals.ThisAddIn._productSelectorWindow;
            var currentViewModel = currentWindow.DataContext as ProductSelectorViewModel;

            // 3. 阻塞等待用户操作
            bool? dialogResult = currentWindow.ShowDialog();

            // 4. 判断结果
            if (dialogResult == true)
            {
                // ★ 5. 跨过 Window，直接从 ViewModel 里把数据掏出来！
                // 因为 ViewModel 不受界面销毁的影响，数据 100% 在这里！
                var selectedData = currentViewModel.SelectedProducts.ToList();

                // [测试专用] 如果你还是怀疑没拿到，把这行取消注释看一眼弹窗：
                // System.Windows.MessageBox.Show($"拿到啦！总共 {selectedData.Count} 条数据");

                if (selectedData.Count > 0)
                {
                    // ========================================
                    // ★ 完美拿到数据，开始写入 Excel 逻辑...
                    // ========================================
                    string 待完成 = "待完成";
                }
            }


        }



        private void CustomerManager_Click(object sender, RibbonControlEventArgs e)
        {
            // new 一个窗口，并把 ViewModel 塞给它的 DataContext
            // (如果你在 DI 里连 View 也注册了，这里也可以直接 DiServices.GetService<CustomersManagerView>())
            var window = DiServices.GetService<CustomerManagerView>();

            // 绑定 Excel 为父窗口，保证模态显示不乱跑
            _ = new WindowInteropHelper(window)
            {
                Owner = new System.IntPtr(Globals.ThisAddIn.Application.Hwnd)
            };
            window.ShowDialog();

        }

        private void SupplierManager_Click(object sender, RibbonControlEventArgs e)
        {
            var _window = DiServices.GetService<SupplierManagerView>();
            // 绑定 Excel 为父窗口，保证模态显示不乱跑
            _ = new System.Windows.Interop.WindowInteropHelper(_window)
            {
                Owner = new System.IntPtr(Globals.ThisAddIn.Application.Hwnd)
            };
            _window.ShowDialog();
        }

        private void ProductColorManager_Click(object sender, RibbonControlEventArgs e)
        {
            var window = DiServices.GetService<ProductColorManagerView>();

            // 绑定 Excel 为父窗口，保证模态显示不乱跑
            _ = new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = new System.IntPtr(Globals.ThisAddIn.Application.Hwnd)
            };
            window.ShowDialog();
        }

        private void PoConfigManager_Click(object sender, RibbonControlEventArgs e)
        {
            var window = DiServices.GetService<CustomerPoConfigManagerView>();

            // 绑定 Excel 为父窗口，保证模态显示不乱跑
            _ = new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = new System.IntPtr(Globals.ThisAddIn.Application.Hwnd)
            };
            window.ShowDialog();
        }
    }
}
