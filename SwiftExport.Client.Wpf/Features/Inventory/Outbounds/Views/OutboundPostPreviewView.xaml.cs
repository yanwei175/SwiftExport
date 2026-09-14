using System.Windows.Controls;

namespace SwiftExportClient.Features.Inventory.Outbounds.Views;

/// <summary>只读过账预览布局，确认按钮由弹窗宿主管理。</summary>
public partial class OutboundPostPreviewView : UserControl
{
    /// <summary>初始化预览布局，不发起网络请求。</summary>
    public OutboundPostPreviewView() => InitializeComponent();
}
