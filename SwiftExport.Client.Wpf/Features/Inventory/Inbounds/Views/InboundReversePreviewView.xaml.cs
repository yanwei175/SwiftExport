using System.Windows.Controls;
namespace SwiftExportClient.Features.Inventory.Inbounds.Views;
/// <summary>反过账预览视图，由弹窗服务注入模型。</summary>
public partial class InboundReversePreviewView : UserControl
{
    /// <summary>加载只读预览与原因输入模板。</summary>
    public InboundReversePreviewView() => InitializeComponent();
}
