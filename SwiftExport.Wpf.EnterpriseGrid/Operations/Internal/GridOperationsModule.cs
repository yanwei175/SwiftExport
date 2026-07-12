using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using SwiftExport.Wpf.EnterpriseGrid.Columns;
using SwiftExport.Wpf.EnterpriseGrid.Core.Internal;
using SwiftExport.Wpf.EnterpriseGrid.Selection;

namespace SwiftExport.Wpf.EnterpriseGrid.Operations.Internal;

internal sealed class GridOperationsModule
{
    private const int HistoryCapacity = 100;
    private readonly DataGrid _dataGrid;
    private readonly Stack<IUndoAction> _undoStack = [];
    private readonly CommandBinding _undoBinding;
    private readonly CommandBinding _fillDownBinding;
    private readonly DependencyPropertyDescriptor? _selectionUnitDescriptor;
    private EditSnapshot? _activeEdit;
    private EditSnapshot? _pendingCommittedEdit;
    private bool _isApplyingHistory;
    private bool _isAttached;

    public GridOperationsModule(DataGrid dataGrid)
    {
        _dataGrid = dataGrid;
        _undoBinding = new CommandBinding(ApplicationCommands.Undo, OnUndoExecuted, OnUndoCanExecute);
        _fillDownBinding = new CommandBinding(
            GridOperations.FillDownCommand,
            OnFillDownExecuted,
            OnFillDownCanExecute);
        _selectionUnitDescriptor = DependencyPropertyDescriptor.FromProperty(
            DataGrid.SelectionUnitProperty,
            typeof(DataGrid));
    }

    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        _isAttached = true;
        _dataGrid.BeginningEdit += OnBeginningEdit;
        _dataGrid.CellEditEnding += OnCellEditEnding;
        _dataGrid.CommandBindings.Add(_undoBinding);
        _dataGrid.CommandBindings.Add(_fillDownBinding);
        ((INotifyCollectionChanged)_dataGrid.Items).CollectionChanged += OnItemsCollectionChanged;
        _selectionUnitDescriptor?.AddValueChanged(_dataGrid, OnSelectionUnitChanged);
        RefreshEnabledState();
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        _dataGrid.BeginningEdit -= OnBeginningEdit;
        _dataGrid.CellEditEnding -= OnCellEditEnding;
        _dataGrid.CommandBindings.Remove(_undoBinding);
        _dataGrid.CommandBindings.Remove(_fillDownBinding);
        ((INotifyCollectionChanged)_dataGrid.Items).CollectionChanged -= OnItemsCollectionChanged;
        _selectionUnitDescriptor?.RemoveValueChanged(_dataGrid, OnSelectionUnitChanged);
        _activeEdit = null;
        _pendingCommittedEdit = null;
        ClearHistory();
        _isAttached = false;
    }

    internal void RefreshEnabledState()
    {
        if (!IsEnabled)
        {
            _activeEdit = null;
            _pendingCommittedEdit = null;
            ClearHistory();
        }

        CommandManager.InvalidateRequerySuggested();
    }

    internal bool CanFillDown() => IsEnabled && SupportsCellSelection && TryCreateFillPlan(out _);

    internal bool FillDown()
    {
        if (!IsEnabled || !SupportsCellSelection)
        {
            return false;
        }

        if (_activeEdit is not null && !_dataGrid.CommitEdit(DataGridEditingUnit.Cell, true))
        {
            return false;
        }

        CompletePendingEdit();
        if (!TryCreateFillPlan(out var plan))
        {
            return false;
        }

        var changes = new List<CellChange>();
        foreach (var column in plan.Columns)
        {
            var path = GetFieldPath(column)!;
            var source = plan.Items[0];
            var sourceValue = PropertyPathAccessor.GetValue(source, path);
            for (var rowIndex = 1; rowIndex < plan.Items.Count; rowIndex++)
            {
                var target = plan.Items[rowIndex];
                var oldValue = PropertyPathAccessor.GetValue(target, path);
                if (Equals(oldValue, sourceValue))
                {
                    continue;
                }

                if (!PropertyPathAccessor.TrySetValue(target, path, sourceValue))
                {
                    RollBack(changes);
                    return false;
                }

                changes.Add(new CellChange(target, path, oldValue, GridColumn.GetKey(column)));
            }
        }

        if (changes.Count > 0)
        {
            Push(new CompositeUndoAction(changes, CaptureCurrentCell()));
        }

        return true;
    }

    internal bool Undo()
    {
        CompletePendingEdit();
        if (!IsEnabled || _undoStack.Count == 0)
        {
            return false;
        }

        var action = _undoStack.Pop();
        _isApplyingHistory = true;
        try
        {
            action.Undo();
            RestoreCurrentCell(action.Cell);
        }
        finally
        {
            _isApplyingHistory = false;
        }

        RefreshCanUndo();
        return true;
    }

    private bool IsEnabled => _isAttached && GridOperations.GetIsEnabled(_dataGrid);

    private bool SupportsCellSelection =>
        _dataGrid.SelectionUnit is DataGridSelectionUnit.Cell or DataGridSelectionUnit.CellOrRowHeader;

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (!IsEnabled || _isApplyingHistory || e.Cancel || e.Row.Item is null)
        {
            _activeEdit = null;
            return;
        }

        var path = GetFieldPath(e.Column);
        _activeEdit = string.IsNullOrWhiteSpace(path) ||
                      !PropertyPathAccessor.CanSetValue(e.Row.Item, path)
            ? null
            : new EditSnapshot(
                e.Row.Item,
                path,
                PropertyPathAccessor.GetValue(e.Row.Item, path),
                GridColumn.GetKey(e.Column));
    }

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        var snapshot = _activeEdit;
        _activeEdit = null;
        if (!IsEnabled || _isApplyingHistory || e.Cancel || e.EditAction != DataGridEditAction.Commit || snapshot is null)
        {
            return;
        }

        _pendingCommittedEdit = snapshot;
        _dataGrid.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, CompletePendingEdit);
    }

    private void CompletePendingEdit()
    {
        var snapshot = _pendingCommittedEdit;
        _pendingCommittedEdit = null;
        if (snapshot is null || _isApplyingHistory || !IsEnabled)
        {
            return;
        }

        var newValue = PropertyPathAccessor.GetValue(snapshot.Item, snapshot.Path);
        if (!Equals(snapshot.OldValue, newValue))
        {
            Push(new CellUndoAction(
                new CellChange(snapshot.Item, snapshot.Path, snapshot.OldValue, snapshot.ColumnKey),
                new GridCellReference(snapshot.Item, snapshot.ColumnKey)));
        }
    }

    private bool TryCreateFillPlan(out FillPlan plan)
    {
        plan = null!;
        if (!SupportsCellSelection)
        {
            return false;
        }

        var selected = _dataGrid.SelectedCells
            .Where(cell => cell.IsValid && IsDataItem(cell.Item) && cell.Column is not null)
            .ToArray();
        if (selected.Length == 0)
        {
            return false;
        }

        var rowIndexes = selected
            .Select(cell => _dataGrid.Items.IndexOf(cell.Item))
            .Where(index => index >= 0)
            .Distinct()
            .Order()
            .ToArray();
        if (rowIndexes.Length < 2 || !AreContiguous(rowIndexes))
        {
            return false;
        }

        var visibleColumns = _dataGrid.Columns
            .Where(column => column.Visibility == Visibility.Visible)
            .OrderBy(column => column.DisplayIndex)
            .ToArray();
        var selectedColumns = selected
            .Select(cell => cell.Column)
            .Distinct()
            .OrderBy(column => column.DisplayIndex)
            .ToArray();
        var columnIndexes = selectedColumns
            .Select(column => Array.IndexOf(visibleColumns, column))
            .ToArray();
        if (columnIndexes.Any(index => index < 0) || !AreContiguous(columnIndexes))
        {
            return false;
        }

        var pairs = selected
            .Select(cell => (_dataGrid.Items.IndexOf(cell.Item), cell.Column))
            .ToHashSet();
        if (pairs.Count != rowIndexes.Length * selectedColumns.Length ||
            rowIndexes.Any(row => selectedColumns.Any(column => !pairs.Contains((row, column)))))
        {
            return false;
        }

        var items = rowIndexes.Select(index => _dataGrid.Items[index]).ToArray();
        var writableColumns = selectedColumns
            .Where(column => !_dataGrid.IsReadOnly && !column.IsReadOnly)
            .Where(column => GetFieldPath(column) is { } path &&
                             items.All(item => PropertyPathAccessor.CanSetValue(item, path)))
            .ToArray();
        if (writableColumns.Length == 0)
        {
            return false;
        }

        plan = new FillPlan(items, writableColumns);
        return true;
    }

    private static bool AreContiguous(IReadOnlyList<int> indexes)
    {
        for (var index = 1; index < indexes.Count; index++)
        {
            if (indexes[index] != indexes[index - 1] + 1)
            {
                return false;
            }
        }

        return true;
    }

    private static string? GetFieldPath(DataGridColumn column)
    {
        var configured = GridColumn.GetFieldPath(column);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return column is DataGridBoundColumn { Binding: Binding binding }
            ? binding.Path?.Path
            : null;
    }

    private void Push(IUndoAction action)
    {
        if (_undoStack.Count >= HistoryCapacity)
        {
            var retained = _undoStack.Reverse().Skip(1).ToArray();
            _undoStack.Clear();
            foreach (var item in retained)
            {
                _undoStack.Push(item);
            }
        }

        _undoStack.Push(action);
        RefreshCanUndo();
    }

    private void ClearHistory()
    {
        _undoStack.Clear();
        RefreshCanUndo();
    }

    private void RefreshCanUndo()
    {
        GridOperations.SetCanUndoFromView(_dataGrid, IsEnabled && _undoStack.Count > 0);
        CommandManager.InvalidateRequerySuggested();
    }

    private GridCellReference? CaptureCurrentCell()
    {
        var current = _dataGrid.CurrentCell;
        return current.IsValid && current.Column is not null
            ? new GridCellReference(current.Item, GridColumn.GetKey(current.Column))
            : null;
    }

    private void RestoreCurrentCell(GridCellReference? cell)
    {
        if (cell?.Item is null || !_dataGrid.Items.Contains(cell.Item))
        {
            return;
        }

        var column = GridColumn.FindByKey(_dataGrid, cell.ColumnKey);
        if (column is null)
        {
            return;
        }

        _dataGrid.CurrentCell = new DataGridCellInfo(cell.Item, column);
        _dataGrid.ScrollIntoView(cell.Item, column);
        _dataGrid.Focus();
    }

    private static void RollBack(IEnumerable<CellChange> changes)
    {
        foreach (var change in changes.Reverse())
        {
            PropertyPathAccessor.TrySetValue(change.Item, change.Path, change.OldValue);
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _activeEdit = null;
        _pendingCommittedEdit = null;
        ClearHistory();
    }

    private static void OnSelectionUnitChanged(object? sender, EventArgs e) =>
        CommandManager.InvalidateRequerySuggested();

    private void OnUndoCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        CompletePendingEdit();
        e.CanExecute = IsEnabled && _undoStack.Count > 0;
    }

    private void OnUndoExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = Undo();
    }

    private void OnFillDownCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CanFillDown();
    }

    private void OnFillDownExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = FillDown();
    }

    private static bool IsDataItem(object? item) =>
        item is not null && !ReferenceEquals(item, CollectionView.NewItemPlaceholder);

    private sealed record EditSnapshot(object Item, string Path, object? OldValue, string? ColumnKey);

    private sealed record CellChange(object Item, string Path, object? OldValue, string? ColumnKey)
    {
        public void Undo() => PropertyPathAccessor.TrySetValue(Item, Path, OldValue);
    }

    private interface IUndoAction
    {
        GridCellReference? Cell { get; }

        void Undo();
    }

    private sealed record CellUndoAction(CellChange Change, GridCellReference? Cell) : IUndoAction
    {
        public void Undo() => Change.Undo();
    }

    private sealed record CompositeUndoAction(
        IReadOnlyList<CellChange> Changes,
        GridCellReference? Cell) : IUndoAction
    {
        public void Undo()
        {
            foreach (var change in Changes.Reverse())
            {
                change.Undo();
            }
        }
    }

    private sealed record FillPlan(IReadOnlyList<object> Items, IReadOnlyList<DataGridColumn> Columns);
}
