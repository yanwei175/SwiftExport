using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Specialized;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Views;

/// <summary>采购订单管理页，负责呈现筛选、采购头主表和采购行从表。</summary>
public partial class PurchaseOrderManagerView : UserControl
{
    /// <summary>初始化采购订单管理页。</summary>
    public PurchaseOrderManagerView() { InitializeComponent(); IsVisibleChanged += OnVisibilityChanged; }
    /// <summary>保活页面隐藏时关闭浮层，保留输入条件。</summary>
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    { if (e.NewValue is false && DataContext is PurchaseOrderManagerViewModel vm) vm.IsMoreFiltersOpen = false; }

    /// <summary>观察原生集合视图，表头筛选及数据替换后按实际可见行更新勾选范围。</summary>
    private void OnOrdersLoaded(object sender, RoutedEventArgs e)
    {
        ((INotifyCollectionChanged)OrdersGrid.Items).CollectionChanged -= OnVisibleOrdersChanged;
        ((INotifyCollectionChanged)OrdersGrid.Items).CollectionChanged += OnVisibleOrdersChanged;
        SyncVisibleOrders();
    }
    private void OnOrdersUnloaded(object sender, RoutedEventArgs e) => ((INotifyCollectionChanged)OrdersGrid.Items).CollectionChanged -= OnVisibleOrdersChanged;
    private void OnVisibleOrdersChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncVisibleOrders();
    private void SyncVisibleOrders()
    {
        if (DataContext is PurchaseOrderManagerViewModel vm) vm.UpdateVisibleOrders(OrdersGrid.Items.OfType<PurchaseOrderManagementRowModel>());
    }

    /// <summary>右击当前行保留已有多选，复制读取原生选区，不改变业务勾选。</summary>
    private void OnGridRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(grid, source) is DataGridRow row)
        {
            var retained = grid.SelectedItems.Contains(row.Item) ? grid.SelectedItems.Cast<object>().ToArray() : [];
            grid.SelectedItem = row.Item;
            foreach (var selected in retained) if (!grid.SelectedItems.Contains(selected)) grid.SelectedItems.Add(selected);
        }
    }

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
