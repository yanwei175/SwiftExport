using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Data;

namespace SwiftExportClient.Resources.Behaviors.Internal;

/// <summary>维护当前视图行号和可选只读列，不写入业务数据。</summary>
internal sealed class GridRowNumberModule
{
    private DataGridTextColumn? _numberColumn;
    private bool IsEnabled => GridRowHeader.GetShowRowNumber(_dataGrid) || GridRowHeader.GetShowRowNumberInColumn(_dataGrid);
    private readonly DataGrid _dataGrid;
    private bool _isAttached;
    private bool _isLoaded;
    private bool _eventsSubscribed;
    private DispatcherOperation? _pendingRefresh;

    /// <summary>绑定唯一宿主，尚不订阅事件。</summary>
    public GridRowNumberModule(DataGrid dataGrid)
    {
        _dataGrid = dataGrid;
    }

    /// <summary>订阅加载卸载，已加载宿主立即启动当前能力。</summary>
    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        _isAttached = true;
        _dataGrid.Loaded += OnLoaded;
        _dataGrid.Unloaded += OnUnloaded;
        _isLoaded = _dataGrid.IsLoaded;
        RefreshEnabledState();
    }

    /// <summary>解除宿主与运行期订阅，清理本适配拥有的状态。</summary>
    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        _dataGrid.Loaded -= OnLoaded;
        _dataGrid.Unloaded -= OnUnloaded;
        Disable();
        _isLoaded = false;
        _isAttached = false;
    }

    /// <summary>宿主重载时恢复订阅，忽略子元素冒泡。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _dataGrid)) return;
        _isLoaded = true;
        RefreshEnabledState();
    }

    /// <summary>宿主卸载时释放运行期订阅和排队工作。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _dataGrid)) return;
        _isLoaded = false;
        Disable();
    }

    /// <summary>按当前开关启动或停止，卸载期间不观察数据。</summary>
    internal void RefreshEnabledState()
    {
        if (!_isAttached || !_isLoaded)
        {
            return;
        }

        UpdateNumberColumn();
        if (IsEnabled)
        {
            Enable();
        }
        else
        {
            Disable();
        }
    }

    /// <summary>监听视图与容器变化，合并本次调度周期内的行号刷新。</summary>
    private void Enable()
    {
        if (!_eventsSubscribed)
        {
            _dataGrid.LoadingRow += OnLoadingRow;
            _dataGrid.UnloadingRow += OnUnloadingRow;
            _dataGrid.Sorting += OnSorting;
            _dataGrid.ItemContainerGenerator.ItemsChanged += OnItemsChanged;
            _eventsSubscribed = true;
        }

        ScheduleRefresh();
    }

    /// <summary>卸载时取消待执行刷新并恢复已生成容器，不影响原生视图。</summary>
    private void Disable()
    {
        RemoveNumberColumn();
        CancelPendingRefresh();
        if (_eventsSubscribed)
        {
            _dataGrid.LoadingRow -= OnLoadingRow;
            _dataGrid.UnloadingRow -= OnUnloadingRow;
            _dataGrid.Sorting -= OnSorting;
            _dataGrid.ItemContainerGenerator.ItemsChanged -= OnItemsChanged;
            _eventsSubscribed = false;
        }

        UpdateRealizedRows(clear: true);
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e) =>
        GridRowHeader.SetRowNumber(e.Row, e.Row.GetIndex() + 1, !GridRowHeader.GetShowRowNumberInColumn(_dataGrid));

    private static void OnUnloadingRow(object? sender, DataGridRowEventArgs e) =>
        GridRowHeader.ClearRowNumber(e.Row);

    private void OnSorting(object sender, DataGridSortingEventArgs e) => ScheduleRefresh();

    private void OnItemsChanged(object? sender, ItemsChangedEventArgs e) => ScheduleRefresh();

    /// <summary>等待原生排序和容器生成完成后刷新，避免重复排队。</summary>
    private void ScheduleRefresh()
    {
        if (!_isAttached || !_isLoaded || !IsEnabled ||
            _pendingRefresh is { Status: DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing })
        {
            return;
        }

        _pendingRefresh = _dataGrid.Dispatcher.BeginInvoke(
            DispatcherPriority.DataBind,
            new Action(() =>
            {
                _pendingRefresh = null;
                if (_isAttached && _isLoaded && IsEnabled)
                {
                    UpdateRealizedRows(clear: false);
                }
            }));
    }

    private void CancelPendingRefresh()
    {
        if (_pendingRefresh is { Status: DispatcherOperationStatus.Pending } operation)
        {
            operation.Abort();
        }

        _pendingRefresh = null;
    }

    /// <summary>只更新已生成行，不为计数生成或缓存虚拟化容器。</summary>
    private void UpdateRealizedRows(bool clear)
    {
        for (var index = 0; index < _dataGrid.Items.Count; index++)
        {
            if (_dataGrid.ItemContainerGenerator.ContainerFromIndex(index) is not DataGridRow row)
            {
                continue;
            }

            if (clear)
            {
                GridRowHeader.ClearRowNumber(row);
            }
            else
            {
                GridRowHeader.SetRowNumber(row, row.GetIndex() + 1, !GridRowHeader.GetShowRowNumberInColumn(_dataGrid));
            }
        }
    }

    /// <summary>仅添加本模块拥有的普通文本列，沿用宿主 Cell 和 Header 主题。</summary>
    private void UpdateNumberColumn()
    {
        if (!GridRowHeader.GetShowRowNumberInColumn(_dataGrid))
        {
            RemoveNumberColumn();
            return;
        }

        if (_numberColumn is not null) return;
        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        textStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 0, 6, 0)));
        _numberColumn = new DataGridTextColumn
        {
            Header = "行号", Width = 48, IsReadOnly = true,
            CanUserSort = false, CanUserReorder = false, CanUserResize = true,
            ElementStyle = textStyle,
            Binding = new Binding
            {
                Path = new PropertyPath("(0)", GridRowHeader.RowNumberProperty),
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1),
                Mode = BindingMode.OneWay
            }
        };
        _dataGrid.Columns.Insert(0, _numberColumn);
        _numberColumn.DisplayIndex = 0;
    }

    /// <summary>移除适配自己的列，不影响业务列或行头配置。</summary>
    private void RemoveNumberColumn()
    {
        if (_numberColumn is null) return;
        _dataGrid.Columns.Remove(_numberColumn);
        _numberColumn = null;
    }
}
