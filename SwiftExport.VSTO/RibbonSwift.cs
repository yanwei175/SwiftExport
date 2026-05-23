using Microsoft.Office.Tools.Ribbon;
using SwiftExport.View.FW.SystemViews.Views;
using SwiftExport.View.FW.WarehouseViews.Views;
using SwiftExport.VSTO.SwiftExportWindowHelps;
using System.Windows;
namespace SwiftExport.VSTO
{
    public partial class RibbonSwift
    {
        private void RibbonSwift_Load(object sender, RibbonUIEventArgs e)
        {

        }

        private void Button1_Click(object sender, RibbonControlEventArgs e)
        {
            WpfWindowHelp.OpenCtrlByFullWindow("入库管理", DiServices.GetService<ReceiptManagerView>());
        }



        private void CustomerManager_Click(object sender, RibbonControlEventArgs e)
        {
            //// new 一个窗口，并把 ViewModel 塞给它的 DataContext
            //// (如果你在 DI 里连 View 也注册了，这里也可以直接 DiServices.GetService<CustomersManagerView>())
            //var window = DiServices.GetService<CustomerManagerView>();

            //// 绑定 Excel 为父窗口，保证模态显示不乱跑
            //_ = new WindowInteropHelper(window)
            //{
            //    Owner = new System.IntPtr(Globals.ThisAddIn.Application.Hwnd)
            //};
            //window.ShowDialog();

        }

        private void SupplierManager_Click(object sender, RibbonControlEventArgs e)
        {
            //var _window = DiServices.GetService<SupplierManagerView>();
            //// 绑定 Excel 为父窗口，保证模态显示不乱跑
            //_ = new System.Windows.Interop.WindowInteropHelper(_window)
            //{
            //    Owner = new System.IntPtr(Globals.ThisAddIn.Application.Hwnd)
            //};
            //_window.ShowDialog();
        }

        private void ProductColorManager_Click(object sender, RibbonControlEventArgs e)
        {
            //var window = DiServices.GetService<ProductColorManagerView>();

            //// 绑定 Excel 为父窗口，保证模态显示不乱跑
            //_ = new System.Windows.Interop.WindowInteropHelper(window)
            //{
            //    Owner = new System.IntPtr(Globals.ThisAddIn.Application.Hwnd)
            //};
            //window.ShowDialog();
        }

        private void PoConfigManager_Click(object sender, RibbonControlEventArgs e)
        {
            //var window = DiServices.GetService<CustomerPoConfigManagerView>();

            //// 绑定 Excel 为父窗口，保证模态显示不乱跑
            //_ = new WindowInteropHelper(window)
            //{
            //    Owner = new System.IntPtr(Globals.ThisAddIn.Application.Hwnd)
            //};
            //window.ShowDialog();
        }

        private void BtnLogin_Click(object sender, RibbonControlEventArgs e)
        {
            WpfWindowHelp.OpenCtrlByWindow("登录", DiServices.GetService<LoginView>(),480,420);
        }

        private void Buttontes_Click(object sender, RibbonControlEventArgs e)
        {
            WpfWindowHelp.OpenCtrlByFullWindow("入库单详情", DiServices.GetService<ReceiptDetailView>());
        }
    }
}
