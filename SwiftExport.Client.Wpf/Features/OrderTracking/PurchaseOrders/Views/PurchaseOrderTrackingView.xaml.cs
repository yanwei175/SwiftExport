using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExportClient.Features.OrderTracking.PurchaseOrders.Models;
using SwiftExportClient.Features.OrderTracking.PurchaseOrders.ViewModels;
using SwiftExportClient.Resources.Commands;

namespace SwiftExportClient.Features.OrderTracking.PurchaseOrders.Views;

/// <summary>只桥接表格选择、可见计数和既有列筛选命令；业务查询仍由只读VM负责。</summary>
public partial class PurchaseOrderTrackingView : UserControl
{
    private PurchaseOrderTrackingViewModel? _viewModel;
    /// <summary>初始化独立采购视角。</summary>
    public PurchaseOrderTrackingView() => InitializeComponent();
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ((INotifyCollectionChanged)OrdersGrid.Items).CollectionChanged += OnOrdersChanged;
        ((INotifyCollectionChanged)LinesGrid.Items).CollectionChanged += OnLinesChanged;
        AttachViewModel();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ((INotifyCollectionChanged)OrdersGrid.Items).CollectionChanged -= OnOrdersChanged;
        ((INotifyCollectionChanged)LinesGrid.Items).CollectionChanged -= OnLinesChanged;
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = null;
    }
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) { if (IsLoaded) AttachViewModel(); }
    private void AttachViewModel()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = DataContext as PurchaseOrderTrackingViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
        UpdateCounts();
    }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(PurchaseOrderTrackingViewModel.IsUpdatingOrders)) { UpdateCounts(); CheckSelectedOrder(); } }
    private void OnOrdersChanged(object? sender, NotifyCollectionChangedEventArgs e) { UpdateCounts(); CheckSelectedOrder(); }
    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateCounts();
    private void UpdateCounts()
    { if (_viewModel is not null) { _viewModel.VisibleOrderCount = OrdersGrid.Items.Count; _viewModel.VisibleLineCount = LinesGrid.Items.Count; } }
    /// <summary>本地列筛选隐藏当前采购时清掉子表，不将旧证据冒充其他可见采购。</summary>
    private void CheckSelectedOrder()
    { if (_viewModel is { IsUpdatingOrders: false, SelectedOrder: not null } && !OrdersGrid.Items.Contains(_viewModel.SelectedOrder)) _viewModel.SelectedOrder = null; }
    private async void OnOrderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, OrdersGrid) || DataContext is not PurchaseOrderTrackingViewModel vm || vm.IsUpdatingOrders) return;
        vm.SelectedOrder = OrdersGrid.SelectedItem as PurchaseOrderTrackingRowModel;
        if (vm.LoadSelectedOrderCommand.CanExecute(null)) await vm.LoadSelectedOrderCommand.ExecuteAsync(null);
    }
    /// <summary>仅真实采购行双击打开相同稳定身份的事实页签，列头和空白不复用旧选择。</summary>
    private async void OnOrderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(OrdersGrid, source) is not DataGridRow { Item: PurchaseOrderTrackingRowModel item }
            || DataContext is not PurchaseOrderTrackingViewModel vm) return;
        vm.SelectedOrder = item;
        if (vm.OpenFactsCommand.CanExecute(null)) await vm.OpenFactsCommand.ExecuteAsync(null);
    }
    /// <summary>右击设定原生当前行并保留既有多选；复制仍读取原生选区。</summary>
    private void OnGridRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(grid, source) is not DataGridRow row) return;
        var retained = grid.SelectedItems.Contains(row.Item) ? grid.SelectedItems.Cast<object>().ToArray() : [];
        grid.SelectedItem = row.Item;
        foreach (var item in retained) if (!grid.SelectedItems.Contains(item)) grid.SelectedItems.Add(item);
    }
    /// <summary>先清各表本地条件，再查询空业务条件，不直接干涉筛选扩展的Filter实现。</summary>
    private async void OnClearFilters(object sender, RoutedEventArgs e)
    {
        foreach (var grid in new[] { OrdersGrid, LinesGrid, ReportsGrid, ReceiptLinesGrid, ReceiptsGrid }) DataGridFilterCommands.Clear.Execute(grid);
        if (DataContext is PurchaseOrderTrackingViewModel vm && vm.ClearFiltersCommand.CanExecute(null)) await vm.ClearFiltersCommand.ExecuteAsync(null);
    }
}
