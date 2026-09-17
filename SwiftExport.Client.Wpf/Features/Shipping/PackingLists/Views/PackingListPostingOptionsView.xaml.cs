using System.Windows.Controls;

namespace SwiftExportClient.Features.Shipping.PackingLists.Views;

/// <summary>仅承载箱单过账交互展示，不执行业务或网络调用。</summary>
public partial class PackingListPostingOptionsView : UserControl
{
    /// <summary>初始化声明式绑定与只读展示。</summary>
    public PackingListPostingOptionsView() => InitializeComponent();
}
