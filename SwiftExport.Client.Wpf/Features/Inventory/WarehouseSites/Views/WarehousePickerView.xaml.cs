using System.Windows.Controls;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.Views;

/// <summary>承载只读仓库单选控件；业务加载和结果接纳由 ViewModel 与 Feature 服务负责。</summary>
public partial class WarehousePickerView : UserControl
{
    /// <summary>初始化仓库选择器视图。</summary>
    public WarehousePickerView() => InitializeComponent();
}
