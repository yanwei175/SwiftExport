using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using SwiftExport.Wpf.EnterpriseGrid.Columns;

namespace SwiftExport.Wpf.EnterpriseGrid.Selection.Internal;

internal sealed class GridSelectionModule
{
    private readonly DataGrid _dataGrid;
    private bool _isAttached;
    private bool _isSynchronizing;
    private DispatcherOperation? _pendingSynchronization;

    public GridSelectionModule(DataGrid dataGrid)
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
        _dataGrid.SelectionChanged += OnSelectionChanged;
        _dataGrid.CurrentCellChanged += OnCurrentCellChanged;
        _dataGrid.AddHandler(
            UIElement.MouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnMouseLeftButtonDown),
            handledEventsToo: true);
        _dataGrid.ItemContainerGenerator.ItemsChanged += OnItemsChanged;
        ApplyInitialState();
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        _dataGrid.SelectionChanged -= OnSelectionChanged;
        _dataGrid.CurrentCellChanged -= OnCurrentCellChanged;
        _dataGrid.RemoveHandler(
            UIElement.MouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnMouseLeftButtonDown));
        _dataGrid.ItemContainerGenerator.ItemsChanged -= OnItemsChanged;
        CancelPendingSynchronization();
        GridSelection.SetNativeCurrentCellStateFromView(_dataGrid, new DataGridCellInfo());
        _isAttached = false;
    }

    internal void ApplyCurrentRow(object? item)
    {
        if (!_isAttached || _isSynchronizing)
        {
            return;
        }

        CancelPendingSynchronization();
        RunSynchronized(() =>
        {
            if (item is not null && !_dataGrid.Items.Contains(item))
            {
                SynchronizeFromGrid();
                return;
            }

            if (_dataGrid.SelectionUnit == DataGridSelectionUnit.FullRow)
            {
                _dataGrid.SelectedItem = item;
            }
            if (item is null)
            {
                _dataGrid.CurrentCell = new DataGridCellInfo();
                SynchronizeFromGrid();
                return;
            }

            _dataGrid.ScrollIntoView(item);
            var currentColumn = _dataGrid.CurrentCell.Column ?? _dataGrid.Columns
                .Where(column => column.Visibility == Visibility.Visible)
                .OrderBy(column => column.DisplayIndex)
                .FirstOrDefault();
            if (currentColumn is not null)
            {
                _dataGrid.CurrentCell = new DataGridCellInfo(item, currentColumn);
            }

            SynchronizeFromGrid();
        });
    }

    internal void ApplyCurrentCell(GridCellReference? cellReference)
    {
        if (!_isAttached || _isSynchronizing)
        {
            return;
        }

        CancelPendingSynchronization();
        RunSynchronized(() =>
        {
            if (cellReference?.Item is null || !_dataGrid.Items.Contains(cellReference.Item))
            {
                _dataGrid.CurrentCell = new DataGridCellInfo();
                SynchronizeFromGrid();
                return;
            }

            var column = GridColumn.FindByKey(_dataGrid, cellReference.ColumnKey);
            if (column is null)
            {
                SynchronizeFromGrid();
                return;
            }

            if (_dataGrid.SelectionUnit == DataGridSelectionUnit.FullRow)
            {
                _dataGrid.SelectedItem = cellReference.Item;
            }
            _dataGrid.CurrentCell = new DataGridCellInfo(cellReference.Item, column);
            _dataGrid.ScrollIntoView(cellReference.Item, column);
            SynchronizeFromGrid();
        });
    }

    internal void ApplyCurrentColumnKey(string? key)
    {
        if (!_isAttached || _isSynchronizing || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var item = GridSelection.GetCurrentRow(_dataGrid) ?? _dataGrid.SelectedItem;
        if (item is null)
        {
            return;
        }

        ApplyCurrentCell(new GridCellReference(item, key));
    }

    internal void SynchronizeCellClick(DependencyObject source)
    {
        if (!_isAttached || !GridSelection.GetIsCellClickSelectionEnabled(_dataGrid))
        {
            return;
        }

        var cell = FindAncestor<DataGridCell>(source);
        var row = FindAncestor<DataGridRow>(source);
        if (cell?.Column is null || row?.Item is null)
        {
            return;
        }

        CancelPendingSynchronization();
        RunSynchronized(() =>
        {
            if (_dataGrid.SelectionUnit == DataGridSelectionUnit.FullRow &&
                _dataGrid.SelectionMode == DataGridSelectionMode.Single)
            {
                _dataGrid.SelectedItem = row.Item;
            }

            _dataGrid.CurrentCell = new DataGridCellInfo(row.Item, cell.Column);
            SynchronizeFromGrid();
        });
    }

    private void ApplyInitialState()
    {
        if (GridSelection.GetCurrentCell(_dataGrid) is { } currentCell)
        {
            ApplyCurrentCell(currentCell);
            return;
        }

        if (GridSelection.GetCurrentRow(_dataGrid) is { } currentRow)
        {
            ApplyCurrentRow(currentRow);
            SynchronizeFromGrid();
            return;
        }

        SynchronizeFromGrid();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ScheduleSynchronization();
    }

    private void OnCurrentCellChanged(object? sender, EventArgs e)
    {
        ScheduleSynchronization();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            // 必须等 DataGridCell 的原生鼠标类处理器完成后再执行后备同步。
            // Preview 阶段预写 CurrentCell 会在同一次点击中提前聚焦已选 Cell，
            // 使原生 DataGridCell 将普通跨 Cell 单击误判为编辑入口。
            SynchronizeCellClick(source);
        }
    }

    private void OnItemsChanged(object? sender, System.Windows.Controls.Primitives.ItemsChangedEventArgs e)
    {
        ScheduleSynchronization();
    }

    private void ScheduleSynchronization()
    {
        if (!_isAttached || _isSynchronizing ||
            _pendingSynchronization is { Status: DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing })
        {
            return;
        }

        // DataGrid 在一次 Cell 输入中可能短暂发布无效 CurrentCell。合并同一轮原生事件，
        // 只把输入处理结束后的稳定状态回写给 ViewModel，避免 SelectedLine 瞬态变为 null。
        _pendingSynchronization = _dataGrid.Dispatcher.BeginInvoke(
            DispatcherPriority.DataBind,
            new Action(() =>
            {
                _pendingSynchronization = null;
                if (_isAttached && !_isSynchronizing)
                {
                    RunSynchronized(SynchronizeFromGrid);
                }
            }));
    }

    private void CancelPendingSynchronization()
    {
        if (_pendingSynchronization is { Status: DispatcherOperationStatus.Pending } operation)
        {
            operation.Abort();
        }

        _pendingSynchronization = null;
    }

    private void SynchronizeFromGrid()
    {
        var nativeCell = _dataGrid.CurrentCell;
        var cellIsCurrent = nativeCell.IsValid &&
            nativeCell.Item is not null &&
            nativeCell.Column is not null &&
            _dataGrid.Items.Contains(nativeCell.Item) &&
            _dataGrid.Columns.Contains(nativeCell.Column);
        var cell = cellIsCurrent ? nativeCell : new DataGridCellInfo();
        var column = cell.Column;
        var row = _dataGrid.SelectionUnit != DataGridSelectionUnit.FullRow && cell.IsValid
            ? cell.Item
            : _dataGrid.SelectedItem ?? (cell.IsValid ? cell.Item : null);
        var columnKey = column is null ? null : GridColumn.GetKey(column);

        GridSelection.SetCurrentRowFromView(_dataGrid, row);
        GridSelection.SetCurrentColumnFromView(_dataGrid, column);
        GridSelection.SetNativeCurrentCellStateFromView(_dataGrid, cell);
        GridSelection.SetCurrentColumnKeyFromView(_dataGrid, columnKey);
        GridSelection.SetCurrentCellFromView(
            _dataGrid,
            cell.Item is null || column is null
                ? null
                : new GridCellReference(cell.Item, columnKey));

        SynchronizeSelectionSnapshot(row, cell, columnKey);
    }

    private void SynchronizeSelectionSnapshot(object? currentRow, DataGridCellInfo currentCell, string? columnKey)
    {
        if (!GridSelection.HasSelectionSnapshotBinding(_dataGrid))
        {
            return;
        }

        var selectedItems = new HashSet<object>();
        if (_dataGrid.SelectionUnit == DataGridSelectionUnit.FullRow)
        {
            foreach (var item in _dataGrid.SelectedItems.Cast<object>())
            {
                AddSelectableItem(selectedItems, item);
            }
        }
        else
        {
            foreach (var selectedCell in _dataGrid.SelectedCells)
            {
                AddSelectableItem(selectedItems, selectedCell.Item);
            }

            // CellOrRowHeader 的行头选择由原生 SelectedItems 表示，需与 Cell 选区合并。
            foreach (var item in _dataGrid.SelectedItems.Cast<object>())
            {
                AddSelectableItem(selectedItems, item);
            }

            if (selectedItems.Count == 0 && currentRow is not null)
            {
                AddSelectableItem(selectedItems, currentRow);
            }
        }

        var rowsInViewOrder = _dataGrid.Items.Cast<object>()
            .Where(item => item != CollectionView.NewItemPlaceholder && selectedItems.Contains(item))
            .ToArray();
        var cellReference = currentCell.IsValid && currentCell.Item is not null && currentCell.Column is not null
            ? new GridCellReference(currentCell.Item, columnKey)
            : null;

        GridSelection.SetSelectionSnapshotFromView(
            _dataGrid,
            new GridSelectionSnapshot(currentRow, cellReference, rowsInViewOrder, _dataGrid.SelectedCells.Count));
    }

    private static void AddSelectableItem(ISet<object> target, object? item)
    {
        if (item is not null && item != CollectionView.NewItemPlaceholder)
        {
            target.Add(item);
        }
    }

    private void RunSynchronized(Action action)
    {
        if (_isSynchronizing)
        {
            return;
        }

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

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };
        }

        return null;
    }
}
