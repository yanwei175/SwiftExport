using System.Windows.Controls;
namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Views;
/// <summary>采购追溯只读视图，业务证据由页签 ViewModel 从专用查询接口读取。</summary>
public partial class PurchaseOrderTraceView : UserControl
{
    /// <summary>初始化只读追溯视图。</summary>
    public PurchaseOrderTraceView() => InitializeComponent();
}
