using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Views;

/// <summary>采购订单管理页，负责呈现筛选、采购头主表和采购行从表。</summary>
public partial class PurchaseOrderManagerView : UserControl
{
    private static readonly IValueConverter PanelOffsetWidthConverter = new NonNegativePanelOffsetConverter();
    private Button? _linesCorner;
    /// <summary>初始化采购订单管理页。</summary>
    public PurchaseOrderManagerView() { InitializeComponent(); IsVisibleChanged += OnVisibilityChanged; }
    /// <summary>保活页面隐藏时关闭浮层，保留输入条件。</summary>
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    { if (e.NewValue is false && DataContext is PurchaseOrderManagerViewModel vm) vm.IsMoreFiltersOpen = false; }

    /// <summary>仅适配当前模板角部按钮的宽度；隐藏行头时舍入后的面板偏移可能略小于零，不能直接作为Width。</summary>
    private void OnLinesLoaded(object sender, RoutedEventArgs e)
    {
        LinesGrid.LayoutUpdated -= OnLinesLayoutUpdated;
        LinesGrid.LayoutUpdated += OnLinesLayoutUpdated;
        ApplyLinesCornerWidth();
    }
    /// <summary>离开页面时解除布局监听并释放旧模板元素；重载仅挂接一次。</summary>
    private void OnLinesUnloaded(object sender, RoutedEventArgs e)
    { LinesGrid.LayoutUpdated -= OnLinesLayoutUpdated; _linesCorner = null; }
    /// <summary>模板重建会卸载旧角部按钮；只在旧元素失效时重新定位，不在每次横滚遍历模板。</summary>
    private void OnLinesLayoutUpdated(object? sender, EventArgs e)
    { if (_linesCorner?.IsLoaded != true) ApplyLinesCornerWidth(); }
    /// <summary>通过模板部件和全选命令定位角部按钮，普通单元格按钮不在该适配范围。</summary>
    private void ApplyLinesCornerWidth()
    {
        LinesGrid.ApplyTemplate();
        if (LinesGrid.Template.FindName("DG_ScrollViewer", LinesGrid) is not ScrollViewer scroll) return;
        scroll.ApplyTemplate();
        if (scroll.Template.FindName("PART_ColumnHeadersPresenter", scroll) is not DataGridColumnHeadersPresenter headers
            || VisualTreeHelper.GetParent(headers) is not Panel chrome) return;
        var corner = chrome.Children.OfType<Button>().FirstOrDefault(x => x.Command == DataGrid.SelectAllCommand);
        if (corner is null || BindingOperations.GetBinding(corner, WidthProperty)?.Path?.Path != nameof(DataGrid.CellsPanelHorizontalOffset)) return;
        // 不修改原生偏移、可见性、命令及滚动模板；正偏移原样保留，仅在像素宽度边界把负值归零。
        corner.SetBinding(WidthProperty, new Binding(nameof(DataGrid.CellsPanelHorizontalOffset))
        { Source = LinesGrid, Mode = BindingMode.OneWay, Converter = PanelOffsetWidthConverter });
        _linesCorner = corner;
    }

    /// <summary>将模板使用的面板位移转换为非负像素宽度，不掩盖其他绑定或非数字错误。</summary>
    private sealed class NonNegativePanelOffsetConverter : IValueConverter
    {
        /// <summary>布局坐标可以为负，元素宽度不能为负；所有正值保持不变。</summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double width ? Math.Max(0d, width) : value;
        /// <summary>模板宽度只有单向展示职责，不回写原生只读偏移。</summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

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
