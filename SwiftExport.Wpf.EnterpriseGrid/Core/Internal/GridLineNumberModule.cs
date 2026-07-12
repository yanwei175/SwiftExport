using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace SwiftExport.Wpf.EnterpriseGrid.Core.Internal;

internal sealed class GridRowNumberModule
{
    private const double DefaultRowNumberHeaderWidth = 48;
    private readonly DataGrid _dataGrid;
    private bool _isAttached;
    private bool _eventsSubscribed;
    private bool _widthAdjusted;
    private double _originalRowHeaderWidth;
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

        if (GridRowHeader.GetShowRowNumber(_dataGrid))
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

        if (!_widthAdjusted)
        {
            _originalRowHeaderWidth = _dataGrid.RowHeaderWidth;
            if (double.IsNaN(_dataGrid.RowHeaderWidth) || _dataGrid.RowHeaderWidth < DefaultRowNumberHeaderWidth)
            {
                _dataGrid.RowHeaderWidth = DefaultRowNumberHeaderWidth;
                _widthAdjusted = true;
            }
        }

        ScheduleRefresh();
    }

    private void Disable()
    {
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
        if (_widthAdjusted)
        {
            _dataGrid.RowHeaderWidth = _originalRowHeaderWidth;
            _widthAdjusted = false;
        }
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e) =>
        GridRowHeader.SetRowNumber(e.Row, e.Row.GetIndex() + 1);

    private static void OnUnloadingRow(object? sender, DataGridRowEventArgs e) =>
        GridRowHeader.ClearRowNumber(e.Row);

    private void OnSorting(object sender, DataGridSortingEventArgs e) => ScheduleRefresh();

    private void OnItemsChanged(object? sender, ItemsChangedEventArgs e) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        if (!_isAttached || !GridRowHeader.GetShowRowNumber(_dataGrid) ||
            _pendingRefresh is { Status: DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing })
        {
            return;
        }

        _pendingRefresh = _dataGrid.Dispatcher.BeginInvoke(
            DispatcherPriority.DataBind,
            new Action(() =>
            {
                _pendingRefresh = null;
                if (_isAttached && GridRowHeader.GetShowRowNumber(_dataGrid))
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
                GridRowHeader.SetRowNumber(row, row.GetIndex() + 1);
            }
        }
    }
}
