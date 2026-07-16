using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace SwiftExport.Wpf.EnterpriseGrid.Operations.Internal;

/// <summary>管理行排序操作列及其只读、排序、筛选安全状态。</summary>
internal sealed class GridLineReorderModule
{
    private static readonly DependencyPropertyDescriptor ItemsSourceDescriptor =
        DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid));

    private static readonly DependencyPropertyDescriptor IsReadOnlyDescriptor =
        DependencyPropertyDescriptor.FromProperty(DataGrid.IsReadOnlyProperty, typeof(DataGrid));

    private static readonly DependencyPropertyDescriptor CanUserSortColumnsDescriptor =
        DependencyPropertyDescriptor.FromProperty(DataGrid.CanUserSortColumnsProperty, typeof(DataGrid));

    private readonly DataGrid _dataGrid;
    private LineReorderDataGridColumn? _column;
    private ICollectionView? _view;
    private INotifyPropertyChanged? _viewPropertyNotifier;
    private INotifyCollectionChanged? _viewCollectionNotifier;
    private INotifyCollectionChanged? _sortNotifier;
    private INotifyCollectionChanged? _groupNotifier;
    private DispatcherOperation? _pendingColumnRefresh;
    private bool _isAttached;
    private bool _isUpdatingColumn;

    public GridLineReorderModule(DataGrid dataGrid)
    {
        _dataGrid = dataGrid;
    }

    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        _isAttached = true;
        ItemsSourceDescriptor.AddValueChanged(_dataGrid, OnItemsSourceChanged);
        IsReadOnlyDescriptor.AddValueChanged(_dataGrid, OnGridConfigurationChanged);
        CanUserSortColumnsDescriptor.AddValueChanged(_dataGrid, OnGridConfigurationChanged);
        ((INotifyCollectionChanged)_dataGrid.Columns).CollectionChanged += OnColumnsChanged;
        AttachView();
        ApplyConfiguration();
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        ItemsSourceDescriptor.RemoveValueChanged(_dataGrid, OnItemsSourceChanged);
        IsReadOnlyDescriptor.RemoveValueChanged(_dataGrid, OnGridConfigurationChanged);
        CanUserSortColumnsDescriptor.RemoveValueChanged(_dataGrid, OnGridConfigurationChanged);
        ((INotifyCollectionChanged)_dataGrid.Columns).CollectionChanged -= OnColumnsChanged;
        _pendingColumnRefresh?.Abort();
        _pendingColumnRefresh = null;
        DetachView();
        RemoveColumn();
        DataGridLineReorder.SetCanReorderFromView(_dataGrid, false);
        _isAttached = false;
    }

    public void ApplyConfiguration()
    {
        if (!_isAttached)
        {
            return;
        }

        if (!DataGridLineReorder.GetIsEnabled(_dataGrid))
        {
            RemoveColumn();
            DataGridLineReorder.SetCanReorderFromView(_dataGrid, false);
            return;
        }

        EnsureColumn();
        UpdateColumnConfiguration();
        RefreshSafetyState();
    }

    private void EnsureColumn()
    {
        if (_column is null)
        {
            _column = new LineReorderDataGridColumn(_dataGrid)
            {
                IsReadOnly = true,
                CanUserSort = false,
                CanUserResize = false,
                CanUserReorder = false,
            };
        }

        if (_dataGrid.Columns.Contains(_column))
        {
            return;
        }

        _isUpdatingColumn = true;
        try
        {
            _dataGrid.Columns.Add(_column);
        }
        finally
        {
            _isUpdatingColumn = false;
        }
    }

    private void RemoveColumn()
    {
        if (_column is null || !_dataGrid.Columns.Contains(_column))
        {
            return;
        }

        _isUpdatingColumn = true;
        try
        {
            _dataGrid.Columns.Remove(_column);
        }
        finally
        {
            _isUpdatingColumn = false;
        }
    }

    private void UpdateColumnConfiguration()
    {
        if (_column is null)
        {
            return;
        }

        _column.Header = DataGridLineReorder.GetColumnHeader(_dataGrid);
        _column.Width = DataGridLineReorder.GetColumnWidth(_dataGrid);
        _column.Visibility = _dataGrid.IsReadOnly && !DataGridLineReorder.GetShowWhenReadOnly(_dataGrid)
            ? Visibility.Collapsed
            : Visibility.Visible;

        var displayIndex = Math.Clamp(
            DataGridLineReorder.GetColumnDisplayIndex(_dataGrid),
            0,
            Math.Max(0, _dataGrid.Columns.Count - 1));
        if (_column.DisplayIndex != displayIndex)
        {
            _column.DisplayIndex = displayIndex;
        }
    }

    private void RefreshSafetyState()
    {
        var isSafe = DataGridLineReorder.GetIsEnabled(_dataGrid)
            && !_dataGrid.IsReadOnly
            && !_dataGrid.CanUserSortColumns
            && !HasViewTransformation(_view);
        DataGridLineReorder.SetCanReorderFromView(_dataGrid, isSafe);
    }

    private static bool HasViewTransformation(ICollectionView? view)
    {
        if (view is null)
        {
            return false;
        }

        return view.SortDescriptions.Count > 0
            || view.Filter is not null
            || view.GroupDescriptions.Count > 0
            || view is ListCollectionView { CustomSort: not null };
    }

    private void OnItemsSourceChanged(object? sender, EventArgs e)
    {
        AttachView();
        RefreshSafetyState();
    }

    private void OnGridConfigurationChanged(object? sender, EventArgs e)
    {
        UpdateColumnConfiguration();
        RefreshSafetyState();
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isUpdatingColumn || !DataGridLineReorder.GetIsEnabled(_dataGrid))
        {
            return;
        }

        if (_pendingColumnRefresh is { Status: DispatcherOperationStatus.Pending })
        {
            return;
        }

        // Columns.CollectionChanged 正在分发时不能可靠地回写同一集合；
        // 合并到下一轮 Dispatcher，避免动态 Clear/Remove 导致重入异常。
        _pendingColumnRefresh = _dataGrid.Dispatcher.BeginInvoke(
            DispatcherPriority.DataBind,
            new Action(() =>
            {
                _pendingColumnRefresh = null;
                ApplyConfiguration();
            }));
    }

    private void OnViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            or nameof(ICollectionView.Filter)
            or nameof(ICollectionView.SortDescriptions)
            or nameof(ICollectionView.GroupDescriptions)
            or nameof(ListCollectionView.CustomSort))
        {
            RefreshSafetyState();
        }
    }

    private void OnViewCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshSafetyState();

    private void AttachView()
    {
        DetachView();
        _view = CollectionViewSource.GetDefaultView(_dataGrid.ItemsSource ?? _dataGrid.Items);
        _viewPropertyNotifier = _view as INotifyPropertyChanged;
        if (_viewPropertyNotifier is not null)
        {
            _viewPropertyNotifier.PropertyChanged += OnViewPropertyChanged;
        }

        _viewCollectionNotifier = _view as INotifyCollectionChanged;
        if (_viewCollectionNotifier is not null)
        {
            _viewCollectionNotifier.CollectionChanged += OnViewCollectionChanged;
        }

        _sortNotifier = _view?.SortDescriptions as INotifyCollectionChanged;
        if (_sortNotifier is not null)
        {
            _sortNotifier.CollectionChanged += OnViewCollectionChanged;
        }

        _groupNotifier = _view?.GroupDescriptions as INotifyCollectionChanged;
        if (_groupNotifier is not null)
        {
            _groupNotifier.CollectionChanged += OnViewCollectionChanged;
        }
    }

    private void DetachView()
    {
        if (_viewPropertyNotifier is not null)
        {
            _viewPropertyNotifier.PropertyChanged -= OnViewPropertyChanged;
        }

        if (_viewCollectionNotifier is not null)
        {
            _viewCollectionNotifier.CollectionChanged -= OnViewCollectionChanged;
        }

        if (_sortNotifier is not null)
        {
            _sortNotifier.CollectionChanged -= OnViewCollectionChanged;
        }

        if (_groupNotifier is not null)
        {
            _groupNotifier.CollectionChanged -= OnViewCollectionChanged;
        }

        _view = null;
        _viewPropertyNotifier = null;
        _viewCollectionNotifier = null;
        _sortNotifier = null;
        _groupNotifier = null;
    }
}
