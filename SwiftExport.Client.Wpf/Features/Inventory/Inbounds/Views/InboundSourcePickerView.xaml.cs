using System.Windows.Controls;

namespace SwiftExportClient.Features.Inventory.Inbounds.Views;

/// <summary>独立入库选源视图；运行时 DataContext 由未来宿主注入，不自行创建业务 ViewModel。</summary>
public partial class InboundSourcePickerView : UserControl
{
    /// <summary>初始化选源布局与绑定，不加载数据、不打开弹窗。</summary>
    public InboundSourcePickerView()
    {
        InitializeComponent();
    }
}
