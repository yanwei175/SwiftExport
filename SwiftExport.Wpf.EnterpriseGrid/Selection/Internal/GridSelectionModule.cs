using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SwiftExport.Wpf.EnterpriseGrid.Columns;

namespace SwiftExport.Wpf.EnterpriseGrid.Selection.Internal;

internal sealed class GridSelectionModule
{
    private readonly DataGrid _dataGrid;
    private bool _isAttached;
    private bool _isSynchronizing;

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
        _dataGrid.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
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
        _dataGrid.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        _isAttached = false;
    }

    internal void ApplyCurrentRow(object? item)
    {
        if (!_isAttached || _isSynchronizing)
        {
            return;
        }

        RunSynchronized(() =>
        {
            if (item is not null && !_dataGrid.Items.Contains(item))
            {
                SynchronizeFromGrid();
                return;
            }

            _dataGrid.SelectedItem = item;
            if (item is null)
            {
                _dataGrid.CurrentCell = new DataGridCellInfo();
                SynchronizeFromGrid();
                return;
            }

            _dataGrid.ScrollIntoView(item);
            if (_dataGrid.CurrentCell.Column is { } currentColumn)
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

            _dataGrid.SelectedItem = cellReference.Item;
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

        RunSynchronized(() =>
        {
            if (_dataGrid.SelectionMode == DataGridSelectionMode.Single)
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
        if (!_isSynchronizing)
        {
            RunSynchronized(SynchronizeFromGrid);
        }
    }

    private void OnCurrentCellChanged(object? sender, EventArgs e)
    {
        if (!_isSynchronizing)
        {
            RunSynchronized(SynchronizeFromGrid);
        }
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            SynchronizeCellClick(source);
        }
    }

    private void SynchronizeFromGrid()
    {
        var cell = _dataGrid.CurrentCell;
        var column = cell.Column;
        var row = _dataGrid.SelectionUnit == DataGridSelectionUnit.Cell && cell.IsValid
            ? cell.Item
            : _dataGrid.SelectedItem ?? (cell.IsValid ? cell.Item : null);
        var columnKey = column is null ? null : GridColumn.GetKey(column);

        GridSelection.SetCurrentRowFromView(_dataGrid, row);
        GridSelection.SetCurrentColumnFromView(_dataGrid, column);
        GridSelection.SetCurrentColumnKeyFromView(_dataGrid, columnKey);
        GridSelection.SetCurrentCellFromView(
            _dataGrid,
            cell.Item is null || column is null
                ? null
                : new GridCellReference(cell.Item, columnKey));
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
