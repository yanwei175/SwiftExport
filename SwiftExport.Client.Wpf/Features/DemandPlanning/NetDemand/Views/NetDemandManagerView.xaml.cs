using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Collections.Specialized;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Views;

/// <summary>
/// 物料需求工作台视图，负责来源选择、可见行快照和原生右键选择的视觉桥接。
/// </summary>
public partial class NetDemandManagerView : UserControl
{
    /// <summary>初始化视图及页签浮层生命周期。</summary>
    public NetDemandManagerView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>页签隐藏时关闭多行筛选 Popup，不清理用户已经输入的筛选条件。</summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false && DataContext is NetDemandManagerViewModel viewModel)
        {
            viewModel.IsMultiLineFilterOpen = false;
        }
    }

    /// <summary>
    /// 选择来源单时加载完整物料族；命令处理未保存确认及失败后的选择恢复。
    /// </summary>
    private async void OnSourceGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is NetDemandManagerViewModel viewModel
            && sender is ListBox { SelectedItem: NetDemandSourceGroupModel group })
        {
            if (viewModel.SelectSourceGroupCommand.CanExecute(group)) await viewModel.SelectSourceGroupCommand.ExecuteAsync(group);
        }
    }

    /// <summary>加载后观察原生集合视图变化，包含列筛选和刷新，不持有行容器。</summary>
    private void OnGridLoaded(object sender, RoutedEventArgs e)
    {
        ((INotifyCollectionChanged)MaterialGrid.Items).CollectionChanged -= OnVisibleRowsChanged;
        ((INotifyCollectionChanged)MaterialGrid.Items).CollectionChanged += OnVisibleRowsChanged;
        SyncVisibleRows();
    }

    /// <summary>卸载时解除视图事件，避免保活页面重复订阅。</summary>
    private void OnGridUnloaded(object sender, RoutedEventArgs e)
        => ((INotifyCollectionChanged)MaterialGrid.Items).CollectionChanged -= OnVisibleRowsChanged;

    private void OnVisibleRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncVisibleRows();

    /// <summary>只把当前视图中的业务模型传给全选逻辑，保持筛选隐藏行不被表头全选。</summary>
    private void SyncVisibleRows()
    {
        if (DataContext is NetDemandManagerViewModel vm)
            vm.UpdateVisibleRows(MaterialGrid.Items.OfType<MaterialDemandManagerRowModel>());
    }

    /// <summary>右击已选行保留原生多选，以便复制所选行，同时把当前行交给跟踪命令。</summary>
    private void OnDemandRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(grid, source) is DataGridRow { Item: MaterialDemandManagerRowModel row })
        {
            var retained = grid.SelectedItems.Contains(row) ? grid.SelectedItems.Cast<object>().ToArray() : [];
            grid.SelectedItem = row;
            foreach (var selected in retained) if (!grid.SelectedItems.Contains(selected)) grid.SelectedItems.Add(selected);
        }
    }
}
