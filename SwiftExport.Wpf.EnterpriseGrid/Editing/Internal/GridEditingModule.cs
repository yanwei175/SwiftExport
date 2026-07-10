using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SwiftExport.Wpf.EnterpriseGrid.Columns;

namespace SwiftExport.Wpf.EnterpriseGrid.Editing.Internal;

internal sealed class GridEditingModule
{
    private readonly DataGrid _dataGrid;
    private bool _isAttached;
    private bool _isEditing;

    public GridEditingModule(DataGrid dataGrid)
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
        _dataGrid.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        _dataGrid.PreviewKeyDown += OnPreviewKeyDown;
        _dataGrid.BeginningEdit += OnBeginningEdit;
        _dataGrid.CellEditEnding += OnCellEditEnding;
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        _dataGrid.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        _dataGrid.PreviewKeyDown -= OnPreviewKeyDown;
        _dataGrid.BeginningEdit -= OnBeginningEdit;
        _dataGrid.CellEditEnding -= OnCellEditEnding;
        _isEditing = false;
        _isAttached = false;
    }

    internal bool HandleDoubleClick(DependencyObject source)
    {
        if (!IsEnabled || !GridEditing.GetBeginEditOnDoubleClick(_dataGrid))
        {
            return false;
        }

        var cell = FindAncestor<DataGridCell>(source);
        if (cell?.Column is null || cell.DataContext is null || !CanEdit(cell.DataContext, cell.Column))
        {
            return false;
        }

        if (!_dataGrid.CurrentCell.IsValid ||
            !ReferenceEquals(_dataGrid.CurrentCell.Item, cell.DataContext) ||
            !ReferenceEquals(_dataGrid.CurrentCell.Column, cell.Column))
        {
            _dataGrid.SelectedItem = cell.DataContext;
            _dataGrid.CurrentCell = new DataGridCellInfo(cell.DataContext, cell.Column);
        }

        return BeginCurrentCellEdit();
    }

    internal bool HandleKey(Key key, ModifierKeys modifiers, DependencyObject? originalSource)
    {
        if (!IsEnabled || HasUnsupportedModifiers(modifiers) || EditorOwnsKey(key, originalSource))
        {
            return false;
        }

        return key switch
        {
            Key.F2 when GridEditing.GetBeginEditOnF2(_dataGrid) => BeginCurrentCellEdit(),
            Key.Enter => HandleEnter(),
            Key.Tab => HandleTab((modifiers & ModifierKeys.Shift) != 0),
            _ => false
        };
    }

    private bool IsEnabled => _isAttached && GridEditing.GetIsEnabled(_dataGrid);

    private bool HandleEnter()
    {
        if (GridEditing.GetEnterKeyMode(_dataGrid) == GridEnterKeyMode.Native)
        {
            return false;
        }

        if (IsCurrentCellEditing())
        {
            var committed = _dataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            _isEditing = !committed;
            return true;
        }

        return BeginCurrentCellEdit();
    }

    private bool HandleTab(bool reverse)
    {
        if (GridEditing.GetTabNavigationMode(_dataGrid) == GridTabNavigationMode.Native ||
            !_dataGrid.CurrentCell.IsValid)
        {
            return false;
        }

        if (IsCurrentCellEditing())
        {
            var committed = _dataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            _isEditing = !committed;
            if (!committed)
            {
                return true;
            }
        }

        if (!TryGetAdjacentCell(reverse, out var targetItem, out var targetColumn))
        {
            return false;
        }

        _dataGrid.SelectedItem = targetItem;
        _dataGrid.ScrollIntoView(targetItem, targetColumn);
        _dataGrid.CurrentCell = new DataGridCellInfo(targetItem, targetColumn);
        _dataGrid.Focus();
        return true;
    }

    private bool BeginCurrentCellEdit()
    {
        var current = _dataGrid.CurrentCell;
        if (!current.IsValid || current.Item is null || current.Column is null ||
            !CanEdit(current.Item, current.Column))
        {
            return false;
        }

        if (IsCurrentCellEditing())
        {
            return true;
        }

        _dataGrid.ScrollIntoView(current.Item, current.Column);
        _dataGrid.Focus();
        var began = _dataGrid.BeginEdit();
        _isEditing = began || IsCurrentCellEditing();
        return _isEditing;
    }

    private bool TryGetAdjacentCell(bool reverse, out object targetItem, out DataGridColumn targetColumn)
    {
        targetItem = null!;
        targetColumn = null!;

        var columns = _dataGrid.Columns
            .Where(IsNavigableColumn)
            .OrderBy(column => column.DisplayIndex)
            .ToArray();
        if (columns.Length == 0)
        {
            return false;
        }

        var current = _dataGrid.CurrentCell;
        var rowIndex = _dataGrid.Items.IndexOf(current.Item);
        var columnIndex = Array.IndexOf(columns, current.Column);
        if (rowIndex < 0 || columnIndex < 0)
        {
            return false;
        }

        var step = reverse ? -1 : 1;
        var flatIndex = (rowIndex * columns.Length) + columnIndex + step;
        var limit = _dataGrid.Items.Count * columns.Length;
        while (flatIndex >= 0 && flatIndex < limit)
        {
            var candidateRowIndex = flatIndex / columns.Length;
            var candidateColumnIndex = flatIndex % columns.Length;
            var candidateItem = _dataGrid.Items[candidateRowIndex];
            var candidateColumn = columns[candidateColumnIndex];
            if (IsDataItem(candidateItem) && CanEdit(candidateItem, candidateColumn))
            {
                targetItem = candidateItem;
                targetColumn = candidateColumn;
                return true;
            }

            flatIndex += step;
        }

        return false;
    }

    private bool IsNavigableColumn(DataGridColumn column) =>
        column.Visibility == Visibility.Visible &&
        !column.IsReadOnly &&
        GridColumn.GetCanNavigate(column);

    private bool CanEdit(object item, DataGridColumn column) =>
        IsDataItem(item) &&
        !_dataGrid.IsReadOnly &&
        IsNavigableColumn(column);

    private static bool IsDataItem(object? item) =>
        item is not null && !ReferenceEquals(item, CollectionView.NewItemPlaceholder);

    private bool IsCurrentCellEditing()
    {
        var content = _dataGrid.CurrentCell.Column?.GetCellContent(_dataGrid.CurrentCell.Item);
        return _isEditing || FindAncestor<DataGridCell>(content)?.IsEditing == true;
    }

    private static bool HasUnsupportedModifiers(ModifierKeys modifiers) =>
        (modifiers & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Windows)) != 0;

    private static bool EditorOwnsKey(Key key, DependencyObject? source)
    {
        if (source is null)
        {
            return false;
        }

        if (key == Key.Enter && FindAncestor<ButtonBase>(source) is not null)
        {
            return true;
        }

        if (FindAncestor<TextBox>(source) is { } textBox)
        {
            return key == Key.Enter && textBox.AcceptsReturn ||
                   key == Key.Tab && textBox.AcceptsTab;
        }

        return key == Key.Enter && FindAncestor<ComboBox>(source)?.IsDropDownOpen == true;
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && e.OriginalSource is DependencyObject source && HandleDoubleClick(source))
        {
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HandleKey(key, e.KeyboardDevice.Modifiers, e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
        }
    }

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        _isEditing = !e.Cancel;
    }

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (!e.Cancel)
        {
            _isEditing = false;
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
