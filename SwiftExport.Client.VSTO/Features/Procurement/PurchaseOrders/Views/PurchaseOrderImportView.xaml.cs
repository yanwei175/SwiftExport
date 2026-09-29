using System.Windows.Controls;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.ViewModels;

namespace SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Views
{
    /// <summary>承载采购导入的静态布局预览；本期不读取 Excel 或业务数据。</summary>
    public partial class PurchaseOrderImportView : UserControl
    {
        /// <summary>绑定页面 Scope 提供的空 ViewModel，供后续审批后扩展。</summary>
        public PurchaseOrderImportView(PurchaseOrderImportViewModel model)
        {
            InitializeComponent();
            DataContext = model;
        }
    }
}
