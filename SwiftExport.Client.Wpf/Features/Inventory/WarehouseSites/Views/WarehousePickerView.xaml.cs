using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.Views;

/// <summary>承载只读仓库单选控件；业务加载和结果接纳由 ViewModel 与 Feature 服务负责。</summary>
public partial class WarehousePickerView : UserControl
{
    /// <summary>初始化仓库选择器视图。</summary>
    public WarehousePickerView() => InitializeComponent();

    /// <summary>仅将有效数据行双击转换为 ViewModel 确认命令；表头、空白和滚动条保持无动作。</summary>
    private void WarehouseGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid
            || FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not { } row
            || row.DataContext is not WarehouseSitePickerDto item
            || DataContext is not WarehousePickerViewModel viewModel)
            return;

        grid.SelectedItem = item;
        if (viewModel.ConfirmCommand.CanExecute(null))
        {
            viewModel.ConfirmCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>沿当前可视树向上定位指定祖先，避免非数据行区域误用旧选中项。</summary>
    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
