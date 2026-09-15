using System.Windows.Controls;

namespace SwiftExportClient.Features.Inventory.Outbounds.Views;

/// <summary>保存前的原因表单，业务校验由弹窗模型负责。</summary>
public partial class OutboundIssueReasonView : UserControl
{
    /// <summary>初始化声明式原因绑定。</summary>
    public OutboundIssueReasonView() => InitializeComponent();
}
