using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SwiftExport.Wpf.EnterpriseGrid.Layout.Internal;

internal sealed class GridLayoutModule
{
    private readonly DataGrid _dataGrid;
    private readonly Dictionary<DataGridColumn, ColumnSubscription> _columnSubscriptions = [];
    private readonly DependencyPropertyDescriptor _frozenCountDescriptor;
    private bool _isAttached;
    private bool _isObserving;
    private bool _isSynchronizing;
    private bool _capturePending;

    public GridLayoutModule(DataGrid dataGrid)
    {
        _dataGrid = dataGrid;
        _frozenCountDescriptor = DependencyPropertyDescriptor.FromProperty(
            DataGrid.FrozenColumnCountProperty,
            typeof(DataGrid));
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
        StopObserving();
        _isAttached = false;
    }

    internal void RefreshEnabledState()
    {
        if (!_isAttached)
        {
            return;
        }

        if (GridLayout.GetIsEnabled(_dataGrid))
        {
            StartObserving();
        }
        else
        {
            StopObserving();
        }
    }

    internal void ApplyState(GridLayoutState? state)
    {
        if (!_isObserving || _isSynchronizing || state is null)
        {
            return;
        }

        RunSynchronized(() => GridLayout.SetApplyResult(_dataGrid, GridLayoutManager.Apply(_dataGrid, state)));
    }

    internal void SetStateFromGrid(GridLayoutState state)
    {
        if (_isSynchronizing)
        {
            return;
        }

        RunSynchronized(() => GridLayout.SetStateFromGrid(_dataGrid, state));
    }

    private void StartObserving()
    {
        if (_isObserving)
        {
            return;
        }

        _isObserving = true;
        _dataGrid.Columns.CollectionChanged += OnColumnsChanged;
        _dataGrid.ColumnDisplayIndexChanged += OnColumnDisplayIndexChanged;
        _dataGrid.Sorting += OnSorting;
        _frozenCountDescriptor.AddValueChanged(_dataGrid, OnLayoutPropertyChanged);
        RefreshColumnSubscriptions();

        if (GridLayout.GetState(_dataGrid) is { } state)
        {
            ApplyState(state);
        }
        else
        {
            CaptureNow();
        }
    }

    private void StopObserving()
    {
        if (!_isObserving)
        {
            return;
        }

        _dataGrid.Columns.CollectionChanged -= OnColumnsChanged;
        _dataGrid.ColumnDisplayIndexChanged -= OnColumnDisplayIndexChanged;
        _dataGrid.Sorting -= OnSorting;
        _frozenCountDescriptor.RemoveValueChanged(_dataGrid, OnLayoutPropertyChanged);
        foreach (var subscription in _columnSubscriptions.Values)
        {
            subscription.Dispose();
        }

        _columnSubscriptions.Clear();
        _isObserving = false;
        _capturePending = false;
    }

    private void RefreshColumnSubscriptions()
    {
        foreach (var removed in _columnSubscriptions.Keys.Except(_dataGrid.Columns.Cast<DataGridColumn>()).ToArray())
        {
            _columnSubscriptions[removed].Dispose();
            _columnSubscriptions.Remove(removed);
        }

        foreach (var column in _dataGrid.Columns)
        {
            if (!_columnSubscriptions.ContainsKey(column))
            {
                _columnSubscriptions.Add(column, new ColumnSubscription(column, OnLayoutPropertyChanged));
            }
        }
    }

    private void ScheduleCapture()
    {
        if (!_isObserving || _isSynchronizing || _capturePending)
        {
            return;
        }

        _capturePending = true;
        _dataGrid.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
        {
            _capturePending = false;
            CaptureNow();
        }));
    }

    private void CaptureNow()
    {
        if (!_isObserving || _isSynchronizing)
        {
            return;
        }

        SetStateFromGrid(GridLayoutManager.Capture(_dataGrid));
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshColumnSubscriptions();
        ScheduleCapture();
    }

    private void OnColumnDisplayIndexChanged(object? sender, DataGridColumnEventArgs e) => ScheduleCapture();

    private void OnSorting(object sender, DataGridSortingEventArgs e) => ScheduleCapture();

    private void OnLayoutPropertyChanged(object? sender, EventArgs e) => ScheduleCapture();

    private void RunSynchronized(Action action)
    {
        _isSynchronizing = true;
        try
        {
            action();
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private sealed class ColumnSubscription : IDisposable
    {
        private static readonly DependencyPropertyDescriptor WidthDescriptor =
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
        private static readonly DependencyPropertyDescriptor VisibilityDescriptor =
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.VisibilityProperty, typeof(DataGridColumn));
        private static readonly DependencyPropertyDescriptor SortDirectionDescriptor =
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.SortDirectionProperty, typeof(DataGridColumn));

        private readonly DataGridColumn _column;
        private readonly EventHandler _handler;

        public ColumnSubscription(DataGridColumn column, EventHandler handler)
        {
            _column = column;
            _handler = handler;
            WidthDescriptor.AddValueChanged(column, handler);
            VisibilityDescriptor.AddValueChanged(column, handler);
            SortDirectionDescriptor.AddValueChanged(column, handler);
        }

        public void Dispose()
        {
            WidthDescriptor.RemoveValueChanged(_column, _handler);
            VisibilityDescriptor.RemoveValueChanged(_column, _handler);
            SortDirectionDescriptor.RemoveValueChanged(_column, _handler);
        }
    }
}
