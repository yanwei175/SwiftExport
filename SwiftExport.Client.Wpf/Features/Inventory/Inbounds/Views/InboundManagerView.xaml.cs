using System.Windows.Controls;

namespace SwiftExportClient.Features.Inventory.Inbounds.Views;

/// <summary>保留用户确定的入库管理布局；新 Manager 接线在后续实现，不再依赖旧工作台。</summary>
public partial class InboundManagerView : UserControl
{
    /// <summary>仅初始化布局，不创建 ViewModel 或请求旧接口。</summary>
    public InboundManagerView() => InitializeComponent();
}
