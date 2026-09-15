using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Data;
using SwiftExport.Wpf.EnterpriseGrid.Columns;

namespace SwiftExport.Wpf.EnterpriseGrid.Core.Internal;

/// <summary>维护当前视图行号和可选只读列，不写入业务数据。</summary>
internal sealed class GridRowNumberModule
{
    private DataGridTextColumn? _numberColumn;
    private bool IsEnabled => GridRowHeader.GetShowRowNumber(_dataGrid) || GridRowHeader.GetShowRowNumberInColumn(_dataGrid);
    private readonly DataGrid _dataGrid;
    private bool _isAttached;
    private bool _eventsSubscribed;
    private DispatcherOperation? _pendingRefresh;

    public GridRowNumberModule(DataGrid dataGrid)
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
        RefreshEnabledState();
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        Disable();
        _isAttached = false;
    }

    internal void RefreshEnabledState()
    {
        if (!_isAttached)
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

    private void ScheduleRefresh()
    {
        if (!_isAttached || !IsEnabled ||
            _pendingRefresh is { Status: DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing })
        {
            return;
        }

        _pendingRefresh = _dataGrid.Dispatcher.BeginInvoke(
            DispatcherPriority.DataBind,
            new Action(() =>
            {
                _pendingRefresh = null;
                if (_isAttached && IsEnabled)
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
        GridColumn.SetCanFillDown(_numberColumn, false);
        GridColumn.SetCanExport(_numberColumn, false);
        GridColumn.SetCanFilter(_numberColumn, false);
        _dataGrid.Columns.Insert(0, _numberColumn);
        _numberColumn.DisplayIndex = 0;
    }

    /// <summary>移除框架自己的列，不影响业务列或行头配置。</summary>
    private void RemoveNumberColumn()
    {
        if (_numberColumn is null) return;
        _dataGrid.Columns.Remove(_numberColumn);
        _numberColumn = null;
    }
}
