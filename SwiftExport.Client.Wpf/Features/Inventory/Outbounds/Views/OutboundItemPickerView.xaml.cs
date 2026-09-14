using System.Windows.Controls;

namespace SwiftExportClient.Features.Inventory.Outbounds.Views;

/// <summary>库存产品选择视图；DataContext 由弹窗服务注入，不自行创建业务 ViewModel。</summary>
public partial class OutboundItemPickerView : UserControl
{
    /// <summary>初始化选源布局与绑定，不加载数据、不打开弹窗。</summary>
    public OutboundItemPickerView()
    {
        InitializeComponent();
    }
}
