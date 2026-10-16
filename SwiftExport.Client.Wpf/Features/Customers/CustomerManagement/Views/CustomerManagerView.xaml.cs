using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;

namespace SwiftExportClient.Features.Customers.CustomerManagement.Views;

/// <summary>承载客户页面；菜单打开时读取原生选区，勾选操作由页面命令执行。</summary>
public partial class CustomerManagerView : UserControl
{
    /// <summary>创建客户管理视图。</summary>
    public CustomerManagerView() => InitializeComponent();

    /// <summary>只在打开菜单时捕获数据项，不缓存容器、不改焦点或选区，也不接管编辑器菜单。</summary>
    private void OnCheckMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu { PlacementTarget: DataGrid grid } menu) return;
        var rows = (grid.SelectionUnit == DataGridSelectionUnit.FullRow
                ? grid.SelectedItems.Cast<object>()
                : grid.SelectedCells.Select(cell => cell.Item))
            .OfType<CustomerLineModel>().Distinct().ToArray();
        foreach (var item in menu.Items.OfType<MenuItem>().Take(3)) item.CommandParameter = rows;
    }

    /// <summary>菜单关闭即释放本次选区，重新打开时读取最新数据，避免长期持有换页前的行。</summary>
    private void OnCheckMenuClosed(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        foreach (var item in menu.Items.OfType<MenuItem>().Take(3)) item.CommandParameter = null;
    }
}
