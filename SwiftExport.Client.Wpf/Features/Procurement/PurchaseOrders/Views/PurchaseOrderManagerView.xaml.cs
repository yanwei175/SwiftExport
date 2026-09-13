using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Views;

/// <summary>采购订单管理页，负责呈现筛选、采购头主表和采购行从表。</summary>
public partial class PurchaseOrderManagerView : UserControl
{
    /// <summary>初始化采购订单管理页。</summary>
    public PurchaseOrderManagerView() => InitializeComponent();

    /// <summary>只允许采购行内容双击打开详情；勾选框双击仅改变批量选择，不触发页面导航。</summary>
    private void OnOrdersPreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source)
            return;
        if (ItemsControl.ContainerFromElement(grid, source) is not DataGridRow
            || FindAncestor<CheckBox>(source) is not null)
            return;
        if (DataContext is not PurchaseOrderManagerViewModel viewModel
            || !viewModel.OpenDetailCommand.CanExecute(null))
            return;

        viewModel.OpenDetailCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>沿可视父级查找指定控件，避免勾选交互冒泡成详情双击。</summary>
    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = GetParent(current))
            if (current is T target) return target;
        return null;
    }

    /// <summary>兼容可视元素与内容元素的父级查找。</summary>
    private static DependencyObject? GetParent(DependencyObject source) =>
        source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(source)
            : LogicalTreeHelper.GetParent(source);
}
