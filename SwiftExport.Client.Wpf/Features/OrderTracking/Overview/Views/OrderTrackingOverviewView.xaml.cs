using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExportClient.Features.OrderTracking.Overview.Models;
using System.Windows.Data;
using SwiftExportClient.Features.OrderTracking.Overview.ViewModels;
using SwiftExportClient.Resources.Commands;

namespace SwiftExportClient.Features.OrderTracking.Overview.Views;

/// <summary>只桥接整行交互和本地表头筛选，不访问业务接口。</summary>
public partial class OrderTrackingOverviewView : UserControl
{
    private OrderTrackingOverviewViewModel? _viewModel;
    /// <summary>初始化来源主从只读布局。</summary>
    public OrderTrackingOverviewView() => InitializeComponent();
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ((INotifyCollectionChanged)SourcesGrid.Items).CollectionChanged -= OnVisibleSourcesChanged;
        ((INotifyCollectionChanged)SourcesGrid.Items).CollectionChanged += OnVisibleSourcesChanged;
        ((INotifyCollectionChanged)TrackingGrid.Items).CollectionChanged -= OnVisibleDemandsChanged;
        ((INotifyCollectionChanged)TrackingGrid.Items).CollectionChanged += OnVisibleDemandsChanged;
        AttachViewModel();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ((INotifyCollectionChanged)SourcesGrid.Items).CollectionChanged -= OnVisibleSourcesChanged;
        ((INotifyCollectionChanged)TrackingGrid.Items).CollectionChanged -= OnVisibleDemandsChanged;
        if (_viewModel is not null) { _viewModel.PropertyChanged -= OnViewModelChanged; _viewModel.Summaries.SourcesUpdated -= OnSourceSummariesUpdated; _viewModel.Summaries.DemandsUpdated -= OnDemandSummariesUpdated; }
        _viewModel = null;
    }
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) { if (IsLoaded) AttachViewModel(); }
    /// <summary>绑定真实视图计数并对称解除旧订阅，不用主表的历史统计推断完整响应条数。</summary>
    private void AttachViewModel()
    {
        if (_viewModel is not null) { _viewModel.PropertyChanged -= OnViewModelChanged; _viewModel.Summaries.SourcesUpdated -= OnSourceSummariesUpdated; _viewModel.Summaries.DemandsUpdated -= OnDemandSummariesUpdated; }
        _viewModel = DataContext as OrderTrackingOverviewViewModel;
        if (_viewModel is not null) { _viewModel.PropertyChanged += OnViewModelChanged; _viewModel.Summaries.SourcesUpdated += OnSourceSummariesUpdated; _viewModel.Summaries.DemandsUpdated += OnDemandSummariesUpdated; }
        UpdateVisibleCount();
    }
    private void OnSourceSummariesUpdated(object? sender, EventArgs e) => CollectionViewSource.GetDefaultView(SourcesGrid.ItemsSource)?.Refresh();
    private void OnDemandSummariesUpdated(object? sender, EventArgs e) { CollectionViewSource.GetDefaultView(TrackingGrid.ItemsSource)?.Refresh(); UpdateVisibleCount(); }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(OrderTrackingOverviewViewModel.LoadedDemandCount)) UpdateVisibleCount(); }
    private void OnVisibleDemandsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateVisibleCount();
    private void UpdateVisibleCount() { if (DataContext is OrderTrackingOverviewViewModel vm) vm.VisibleDemandCount = TrackingGrid.Items.Count; }
    /// <summary>本页筛选隐藏已选来源时立即清空子表，不能继续显示被隐藏来源的需求。</summary>
    private void OnVisibleSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is OrderTrackingOverviewViewModel vm && vm.SelectedSource is not null && !SourcesGrid.Items.Contains(vm.SelectedSource))
            vm.SelectedSource = null;
    }
    /// <summary>来源选择只触发当前来源的独立子表加载。</summary>
    private async void OnSourceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, SourcesGrid) || DataContext is not OrderTrackingOverviewViewModel vm) return;
        vm.SelectedSource = SourcesGrid.SelectedItem as OrderTrackingSourceRowModel;
        if (vm.LoadSelectedSourceCommand.CanExecute(null)) await vm.LoadSelectedSourceCommand.ExecuteAsync(null);
    }
    /// <summary>先用现有命令清两表的本地列筛选，再重置业务查询条件。</summary>
    private async void OnClearFilters(object sender, RoutedEventArgs e)
    {
        DataGridFilterCommands.Clear.Execute(SourcesGrid); DataGridFilterCommands.Clear.Execute(TrackingGrid);
        if (DataContext is OrderTrackingOverviewViewModel vm && vm.ClearFiltersCommand.CanExecute(null)) await vm.ClearFiltersCommand.ExecuteAsync(null);
    }
    /// <summary>仅双击真实需求行打开明细，列头和空白不会使用旧选择。</summary>
    private async void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(TrackingGrid, source) is not DataGridRow { Item: OrderTrackingDemandRowModel item }
            || DataContext is not OrderTrackingOverviewViewModel vm) return;
        vm.SelectedItem = item;
        if (vm.OpenSelectedCommand.CanExecute(null)) await vm.OpenSelectedCommand.ExecuteAsync(null);
    }
    /// <summary>右击整行明确当前需求，不增加批量业务动作。</summary>
    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is OrderTrackingOverviewViewModel vm && e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(TrackingGrid, source) is DataGridRow { Item: OrderTrackingDemandRowModel item }) vm.SelectedItem = item;
    }
}
