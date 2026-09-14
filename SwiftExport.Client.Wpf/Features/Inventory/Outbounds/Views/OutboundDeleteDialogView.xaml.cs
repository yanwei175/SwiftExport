using System.Windows.Controls;

namespace SwiftExportClient.Features.Inventory.Outbounds.Views;

/// <summary>出库删除原因表单，不直接调用业务接口。</summary>
public partial class OutboundDeleteDialogView : UserControl
{
    /// <summary>初始化独立删除原因布局。</summary>
    public OutboundDeleteDialogView() => InitializeComponent();
}
