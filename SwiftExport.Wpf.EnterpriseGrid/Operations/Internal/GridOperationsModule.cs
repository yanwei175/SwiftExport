using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly CommandBinding _copyBinding;
    private readonly CommandBinding _pasteBinding;
    private readonly CommandBinding _deleteBinding;
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
        _copyBinding = new CommandBinding(ApplicationCommands.Copy, OnCopyExecuted, OnCopyCanExecute);
        _pasteBinding = new CommandBinding(ApplicationCommands.Paste, OnPasteExecuted, OnPasteCanExecute);
        _deleteBinding = new CommandBinding(ApplicationCommands.Delete, OnDeleteExecuted, OnDeleteCanExecute);
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
        _dataGrid.CommandBindings.Add(_copyBinding);
        _dataGrid.CommandBindings.Add(_pasteBinding);
        _dataGrid.CommandBindings.Add(_deleteBinding);
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
        _dataGrid.CommandBindings.Remove(_copyBinding);
        _dataGrid.CommandBindings.Remove(_pasteBinding);
        _dataGrid.CommandBindings.Remove(_deleteBinding);
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

    /// <summary>向允许写入的目标单元格填充，跳过受限目标，成功改动仍作为一次操作撤销。</summary>
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
        try
        {
            foreach (var column in plan.Columns)
            {
                var source = plan.Items[0];
                foreach (var path in column.FieldPaths)
                {
                    var sourceValue = PropertyPathAccessor.GetValue(source, path);
                    foreach (var target in column.TargetItems)
                    {
                        var oldValue = PropertyPathAccessor.GetValue(target, path);
                        if (Equals(oldValue, sourceValue))
                        {
                            continue;
                        }

                        var change = new CellChange(
                            target,
                            path,
                            oldValue,
                            GridColumn.GetKey(column.Column));
                        changes.Add(change);
                        if (!PropertyPathAccessor.TrySetValue(target, path, sourceValue))
                        {
                            RollBack(changes);
                            return false;
                        }

                    }
                }
            }
        }
        catch
        {
            RollBack(changes);
            return false;
        }

        if (changes.Count > 0)
        {
            Push(new CompositeUndoAction(changes, CaptureCurrentCell()));
        }

        return true;
    }

    internal bool CanCopy() => IsEnabled && SupportsCellSelection && TryCreateCopyPlan(out _);

    internal bool TryBuildCopyText(out string text)
    {
        text = string.Empty;
        if (!IsEnabled || !SupportsCellSelection || !TryCreateCopyPlan(out var plan))
        {
            return false;
        }

        var rows = plan.Items
            .Select(item => (IReadOnlyList<string>)plan.Columns
                .Select(column => FormatClipboardValue(
                    PropertyPathAccessor.GetValue(item, GetFieldPath(column))))
                .ToArray())
            .ToArray();
        text = ClipboardTsvSerializer.Serialize(rows);
        return true;
    }

    internal bool CanPasteText(string text) =>
        IsEnabled && SupportsCellSelection &&
        ClipboardTsvSerializer.TryParse(text, out var values) &&
        TryCreatePastePlan(values, out _);

    internal bool PasteText(string text)
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
        if (!ClipboardTsvSerializer.TryParse(text, out var values) ||
            !TryCreatePastePlan(values, out var plan))
        {
            return false;
        }

        var changes = new List<CellChange>();
        try
        {
            foreach (var assignment in plan.Assignments)
            {
                if (Equals(assignment.OldValue, assignment.NewValue))
                {
                    continue;
                }

                var change = new CellChange(
                    assignment.Item,
                    assignment.Path,
                    assignment.OldValue,
                    GridColumn.GetKey(assignment.Column));
                changes.Add(change);
                if (!PropertyPathAccessor.TrySetValue(assignment.Item, assignment.Path, assignment.NewValue))
                {
                    RollBack(changes);
                    return false;
                }
            }
        }
        catch
        {
            RollBack(changes);
            return false;
        }

        if (changes.Count > 0)
        {
            Push(new CompositeUndoAction(changes, plan.StartCell));
        }

        SelectPasteRange(plan);
        return true;
    }

    internal bool CanClearSelectedCells() =>
        IsEnabled && SupportsCellSelection && TryCreateClearPlan(out _);

    internal bool ClearSelectedCells()
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
        if (!TryCreateClearPlan(out var plan))
        {
            return false;
        }

        var changes = new List<CellChange>();
        try
        {
            foreach (var assignment in plan.Assignments)
            {
                if (Equals(assignment.OldValue, assignment.NewValue))
                {
                    continue;
                }

                var change = new CellChange(
                    assignment.Item,
                    assignment.Path,
                    assignment.OldValue,
                    GridColumn.GetKey(assignment.Column));
                changes.Add(change);
                if (!PropertyPathAccessor.TrySetValue(assignment.Item, assignment.Path, assignment.NewValue))
                {
                    RollBack(changes);
                    return false;
                }
            }
        }
        catch
        {
            RollBack(changes);
            return false;
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
            .Where(column => !_dataGrid.IsReadOnly && !column.IsReadOnly && GridColumn.GetCanFillDown(column))
            // 在写入前按目标行快照许可，避免复合字段 setter 改变状态后只填了一部分字段。
            .Select(column => new FillDownColumnPlan(column, GetFillDownFieldPaths(column),
                items.Skip(1).Where(item => IsFillDownTargetAllowed(column, item)).ToArray()))
            .Where(column => column.TargetItems.Count > 0 && column.FieldPaths.Count > 0 &&
                             column.FieldPaths.All(path => items.All(item =>
                                 PropertyPathAccessor.CanSetValue(item, path) &&
                                 PropertyPathAccessor.TryGetValueType(item, path, out _))))
            .ToArray();
        if (writableColumns.Length == 0)
        {
            return false;
        }

        plan = new FillPlan(items, writableColumns);
        return true;
    }

    /// <summary>仅从目标数据项读取许可，兼容尚未生成控件的虚拟化行；无效配置按禁止处理。</summary>
    private static bool IsFillDownTargetAllowed(DataGridColumn column, object item)
    {
        var path = GridColumn.GetCanFillDownPath(column);
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        try
        {
            return PropertyPathAccessor.GetValue(item, path) is true;
        }
        catch
        {
            // 许可 getter 失败不能导致命令查询崩溃，更不能降级为允许写入。
            return false;
        }
    }

    private bool TryCreateCopyPlan(out SelectionRectangle plan)
    {
        plan = null!;
        if (!TryGetSelectionRectangle(out var selection))
        {
            return false;
        }

        foreach (var column in selection.Columns)
        {
            var path = GetFieldPath(column);
            if (string.IsNullOrWhiteSpace(path) ||
                selection.Items.Any(item => !PropertyPathAccessor.TryGetValueType(item, path, out _)))
            {
                return false;
            }
        }

        plan = selection;
        return true;
    }

    private bool TryCreatePastePlan(
        IReadOnlyList<IReadOnlyList<string>> values,
        out PastePlan plan)
    {
        plan = null!;
        if (!SupportsCellSelection || values.Count == 0 || values[0].Count == 0 || _dataGrid.IsReadOnly)
        {
            return false;
        }

        IReadOnlyList<object> targetItems;
        IReadOnlyList<DataGridColumn> targetColumns;
        var current = _dataGrid.CurrentCell;

        if (values.Count == 1 && values[0].Count == 1 &&
            TryGetSelectionRectangle(out var selectedRectangle) &&
            selectedRectangle.Items.Count * selectedRectangle.Columns.Count > 1)
        {
            targetItems = selectedRectangle.Items;
            targetColumns = selectedRectangle.Columns;
        }
        else
        {
            if (!current.IsValid || !IsDataItem(current.Item) || current.Column is null)
            {
                return false;
            }

            var startRow = _dataGrid.Items.IndexOf(current.Item);
            var visibleColumns = GetVisibleColumns();
            var startColumn = Array.IndexOf(visibleColumns, current.Column);
            if (startRow < 0 || startColumn < 0 ||
                startRow + values.Count > _dataGrid.Items.Count ||
                startColumn + values[0].Count > visibleColumns.Length)
            {
                return false;
            }

            targetItems = Enumerable.Range(startRow, values.Count)
                .Select(index => _dataGrid.Items[index])
                .ToArray();
            targetColumns = visibleColumns.Skip(startColumn).Take(values[0].Count).ToArray();
        }

        if (targetItems.Any(item => !IsDataItem(item)))
        {
            return false;
        }

        var assignments = new List<PasteAssignment>(targetItems.Count * targetColumns.Count);
        for (var rowIndex = 0; rowIndex < targetItems.Count; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < targetColumns.Count; columnIndex++)
            {
                var item = targetItems[rowIndex];
                var column = targetColumns[columnIndex];
                var path = GetFieldPath(column);
                var sourceText = values.Count == 1 && values[0].Count == 1
                    ? values[0][0]
                    : values[rowIndex][columnIndex];
                if (column.IsReadOnly || string.IsNullOrWhiteSpace(path) ||
                    !PropertyPathAccessor.CanSetValue(item, path) ||
                    !TryConvertClipboardValue(item, path, sourceText, out var convertedValue))
                {
                    return false;
                }

                assignments.Add(new PasteAssignment(
                    item,
                    column,
                    path,
                    PropertyPathAccessor.GetValue(item, path),
                    convertedValue));
            }
        }

        var startCell = new GridCellReference(
            targetItems[0],
            GridColumn.GetKey(targetColumns[0]));
        plan = new PastePlan(assignments, targetItems, targetColumns, startCell);
        return true;
    }

    private bool TryCreateClearPlan(out ClearPlan plan)
    {
        plan = null!;
        if (!SupportsCellSelection || _dataGrid.IsReadOnly)
        {
            return false;
        }

        var selected = _dataGrid.SelectedCells
            .Where(cell => cell.IsValid && IsDataItem(cell.Item) && cell.Column is not null)
            .Distinct()
            .OrderBy(cell => _dataGrid.Items.IndexOf(cell.Item))
            .ThenBy(cell => cell.Column.DisplayIndex)
            .ToArray();
        if (selected.Length == 0)
        {
            return false;
        }

        var assignments = new List<ClearAssignment>(selected.Length);
        foreach (var cell in selected)
        {
            // Delete 清空只面向可编辑的原生文本列；模板、复选框、下拉和 Lookup 列不参与。
            if (cell.Column is not DataGridTextColumn textColumn || textColumn.IsReadOnly)
            {
                return false;
            }

            var path = GetFieldPath(textColumn);
            if (string.IsNullOrWhiteSpace(path) ||
                !PropertyPathAccessor.CanSetValue(cell.Item, path) ||
                !PropertyPathAccessor.TryGetValueType(cell.Item, path, out var valueType))
            {
                return false;
            }

            assignments.Add(new ClearAssignment(
                cell.Item,
                textColumn,
                path,
                PropertyPathAccessor.GetValue(cell.Item, path),
                GetClearedValue(valueType)));
        }

        plan = new ClearPlan(assignments);
        return true;
    }

    private static object? GetClearedValue(Type valueType)
    {
        if (valueType == typeof(string))
        {
            return string.Empty;
        }

        if (!valueType.IsValueType || Nullable.GetUnderlyingType(valueType) is not null)
        {
            return null;
        }

        return Activator.CreateInstance(valueType);
    }

    private bool TryGetSelectionRectangle(out SelectionRectangle rectangle)
    {
        rectangle = null!;
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
        var visibleColumns = GetVisibleColumns();
        var columns = selected
            .Select(cell => cell.Column)
            .Distinct()
            .OrderBy(column => column.DisplayIndex)
            .ToArray();
        var columnIndexes = columns.Select(column => Array.IndexOf(visibleColumns, column)).ToArray();
        if (rowIndexes.Length == 0 || columns.Length == 0 ||
            !AreContiguous(rowIndexes) || columnIndexes.Any(index => index < 0) || !AreContiguous(columnIndexes))
        {
            return false;
        }

        var pairs = selected
            .Select(cell => (_dataGrid.Items.IndexOf(cell.Item), cell.Column))
            .ToHashSet();
        if (pairs.Count != rowIndexes.Length * columns.Length ||
            rowIndexes.Any(row => columns.Any(column => !pairs.Contains((row, column)))))
        {
            return false;
        }

        rectangle = new SelectionRectangle(
            rowIndexes.Select(index => _dataGrid.Items[index]).ToArray(),
            columns);
        return true;
    }

    private DataGridColumn[] GetVisibleColumns() =>
        _dataGrid.Columns
            .Where(column => column.Visibility == Visibility.Visible)
            .OrderBy(column => column.DisplayIndex)
            .ToArray();

    private void SelectPasteRange(PastePlan plan)
    {
        _dataGrid.SelectedCells.Clear();
        foreach (var item in plan.Items)
        {
            foreach (var column in plan.Columns)
            {
                _dataGrid.SelectedCells.Add(new DataGridCellInfo(item, column));
            }
        }

        var firstItem = plan.Items[0];
        var firstColumn = plan.Columns[0];
        _dataGrid.CurrentCell = new DataGridCellInfo(firstItem, firstColumn);
        _dataGrid.ScrollIntoView(firstItem, firstColumn);
        _dataGrid.Focus();
    }

    private static string FormatClipboardValue(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture) ?? string.Empty,
        _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
    };

    private static bool TryConvertClipboardValue(
        object item,
        string path,
        string text,
        out object? value)
    {
        value = null;
        if (!PropertyPathAccessor.TryGetValueType(item, path, out var propertyType))
        {
            return false;
        }

        var underlyingType = Nullable.GetUnderlyingType(propertyType);
        var targetType = underlyingType ?? propertyType;
        if (targetType == typeof(string))
        {
            value = text;
            return true;
        }

        if (text.Length == 0 && (underlyingType is not null || !propertyType.IsValueType))
        {
            value = null;
            return true;
        }

        try
        {
            if (targetType.IsEnum)
            {
                value = Enum.Parse(targetType, text, ignoreCase: true);
                return true;
            }

            var converter = TypeDescriptor.GetConverter(targetType);
            if (converter.CanConvertFrom(typeof(string)))
            {
                value = converter.ConvertFrom(null, CultureInfo.CurrentCulture, text);
                return value is not null;
            }

            value = Convert.ChangeType(text, targetType, CultureInfo.CurrentCulture);
            return value is not null;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or NotSupportedException or ArgumentException)
        {
            value = null;
            return false;
        }
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

    private static IReadOnlyList<string> GetFillDownFieldPaths(DataGridColumn column)
    {
        var configured = GridColumn.GetFillDownFieldPaths(column);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured
                .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var fieldPath = GetFieldPath(column);
        return string.IsNullOrWhiteSpace(fieldPath) ? [] : [fieldPath];
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

    private void OnCopyCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (!IsEnabled || IsInputOwnedByEditor())
        {
            return;
        }

        e.CanExecute = CanCopy();
        e.Handled = true;
    }

    private void OnCopyExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (!TryBuildCopyText(out var text))
        {
            e.Handled = true;
            return;
        }

        try
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, text);
            data.SetData(DataFormats.Text, text);
            Clipboard.SetDataObject(data, copy: true);
        }
        catch (ExternalException)
        {
        }

        e.Handled = true;
    }

    private void OnPasteCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (!IsEnabled || IsInputOwnedByEditor())
        {
            return;
        }

        e.CanExecute = SupportsCellSelection &&
                       _dataGrid.CurrentCell.IsValid &&
                       TryGetClipboardText(out _);
        e.Handled = true;
    }

    private void OnPasteExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = TryGetClipboardText(out var text) && PasteText(text);
        if (!e.Handled)
        {
            // GridOperations 已接管 Grid 级 Paste；失败时也不能继续落到其他 DataGrid handler。
            e.Handled = true;
        }
    }

    private void OnDeleteCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (!IsEnabled || IsInputOwnedByEditor())
        {
            return;
        }

        e.CanExecute = CanClearSelectedCells();
        e.Handled = true;
    }

    private void OnDeleteExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (!IsEnabled || IsInputOwnedByEditor())
        {
            return;
        }

        ClearSelectedCells();
        // GridOperations 已接管 Grid 级 Delete；失败时也禁止回落为 DataGrid 的删除行行为。
        e.Handled = true;
    }

    private static bool TryGetClipboardText(out string text)
    {
        text = string.Empty;
        try
        {
            if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                text = Clipboard.GetText(TextDataFormat.UnicodeText);
                return true;
            }

            if (Clipboard.ContainsText(TextDataFormat.Text))
            {
                text = Clipboard.GetText(TextDataFormat.Text);
                return true;
            }
        }
        catch (ExternalException)
        {
        }

        return false;
    }

    private static bool IsInputOwnedByEditor()
    {
        if (Keyboard.FocusedElement is not DependencyObject focused)
        {
            return false;
        }

        if (focused is TextBoxBase or PasswordBox)
        {
            return true;
        }

        var current = focused;
        while (current is not null)
        {
            if (current is DataGridCell cell)
            {
                return cell.IsEditing && !ReferenceEquals(focused, cell);
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : (current as FrameworkContentElement)?.Parent;
        }

        return false;
    }

    private static bool IsDataItem(object? item) =>
        item is not null && !ReferenceEquals(item, CollectionView.NewItemPlaceholder);

    private sealed record EditSnapshot(object Item, string Path, object? OldValue, string? ColumnKey);

    private sealed record SelectionRectangle(
        IReadOnlyList<object> Items,
        IReadOnlyList<DataGridColumn> Columns);

    private sealed record PasteAssignment(
        object Item,
        DataGridColumn Column,
        string Path,
        object? OldValue,
        object? NewValue);

    private sealed record PastePlan(
        IReadOnlyList<PasteAssignment> Assignments,
        IReadOnlyList<object> Items,
        IReadOnlyList<DataGridColumn> Columns,
        GridCellReference StartCell);

    private sealed record ClearAssignment(
        object Item,
        DataGridTextColumn Column,
        string Path,
        object? OldValue,
        object? NewValue);

    private sealed record ClearPlan(IReadOnlyList<ClearAssignment> Assignments);

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

    private sealed record FillPlan(IReadOnlyList<object> Items, IReadOnlyList<FillDownColumnPlan> Columns);

    private sealed record FillDownColumnPlan(
        DataGridColumn Column,
        IReadOnlyList<string> FieldPaths,
        IReadOnlyList<object> TargetItems);
}
