using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SwiftExport.Wpf.EnterpriseGrid.Columns;

namespace SwiftExport.Wpf.EnterpriseGrid.Layout;

/// <summary>捕获和恢复原生 DataGridColumn 布局，不保存业务数据。</summary>
public static class GridLayoutManager
{
    /// <summary>当前支持的布局序列化版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>捕获当前可持久化布局；没有稳定 ColumnKey 的列不会进入快照。</summary>
    public static GridLayoutState Capture(DataGrid dataGrid)
    {
        ArgumentNullException.ThrowIfNull(dataGrid);
        EnsureUniqueColumnKeys(dataGrid);

        var view = CollectionViewSource.GetDefaultView(dataGrid.ItemsSource ?? dataGrid.Items);
        var sortIndexes = view.SortDescriptions
            .Select((sort, index) => (sort.PropertyName, Index: index))
            .ToDictionary(pair => pair.PropertyName, pair => pair.Index, StringComparer.Ordinal);
        var layouts = dataGrid.Columns
            .Select((column, index) => CreateLayout(column, sortIndexes, index))
            .Where(layout => layout is not null)
            .Cast<GridColumnLayout>()
            .ToArray();
        return new GridLayoutState(CurrentVersion, layouts, dataGrid.FrozenColumnCount);
    }

    /// <summary>恢复布局。验证失败时保留当前编辑和布局。</summary>
    public static GridLayoutApplyResult Apply(DataGrid dataGrid, GridLayoutState state)
    {
        ArgumentNullException.ThrowIfNull(dataGrid);
        ArgumentNullException.ThrowIfNull(state);
        if (state.Version > CurrentVersion)
        {
            return GridLayoutApplyResult.Failure($"布局版本 {state.Version} 高于当前支持版本 {CurrentVersion}。");
        }

        if (!dataGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
            !dataGrid.CommitEdit(DataGridEditingUnit.Row, true))
        {
            return GridLayoutApplyResult.Failure("当前编辑未通过验证，列布局未恢复。");
        }

        try
        {
            EnsureUniqueColumnKeys(dataGrid);
            var duplicateStateKey = state.Columns
                .GroupBy(layout => layout.ColumnKey, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1)?.Key;
            if (duplicateStateKey is not null)
            {
                return GridLayoutApplyResult.Failure($"布局中存在重复 ColumnKey：{duplicateStateKey}");
            }

            var invalidWidth = state.Columns.FirstOrDefault(layout =>
                !double.IsFinite(layout.Width.Value) ||
                layout.Width.Unit == GridColumnWidthUnit.Pixel && layout.Width.Value < 0 ||
                layout.Width.Unit == GridColumnWidthUnit.Star && layout.Width.Value <= 0);
            if (invalidWidth is not null)
            {
                return GridLayoutApplyResult.Failure($"列 {invalidWidth.ColumnKey} 包含无效宽度。");
            }

            var columnsByKey = dataGrid.Columns
                .Where(column => !string.IsNullOrWhiteSpace(GridColumn.GetKey(column)))
                .ToDictionary(column => GridColumn.GetKey(column)!, StringComparer.Ordinal);
            var stateByKey = state.Columns.ToDictionary(layout => layout.ColumnKey, StringComparer.Ordinal);
            var knownLayouts = state.Columns.Where(layout => columnsByKey.ContainsKey(layout.ColumnKey)).ToArray();
            if (knownLayouts.Any(layout => layout.DisplayIndex < 0) ||
                knownLayouts.GroupBy(layout => layout.DisplayIndex).Any(group => group.Count() > 1))
            {
                var indexes = string.Join(", ", knownLayouts.Select(layout => $"{layout.ColumnKey}:{layout.DisplayIndex}"));
                return GridLayoutApplyResult.Failure($"布局中的已知列包含无效或重复 DisplayIndex：{indexes}");
            }

            var ignored = state.Columns
                .Where(layout => !columnsByKey.ContainsKey(layout.ColumnKey))
                .Select(layout => layout.ColumnKey)
                .ToArray();

            foreach (var pair in columnsByKey)
            {
                if (!stateByKey.TryGetValue(pair.Key, out var layout))
                {
                    continue;
                }

                pair.Value.Visibility = layout.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                pair.Value.Width = ToDataGridLength(layout.Width);
            }

            var orderedColumns = dataGrid.Columns
                .Select((column, index) => new
                {
                    Column = column,
                    Desired = GridColumn.GetKey(column) is { } key && stateByKey.TryGetValue(key, out var layout)
                        ? layout.DisplayIndex
                        : GetDisplayIndex(column, index),
                    Current = GetDisplayIndex(column, index)
                })
                .OrderBy(item => item.Desired)
                .ThenBy(item => item.Current)
                .Select(item => item.Column)
                .ToArray();
            for (var index = 0; index < orderedColumns.Length; index++)
            {
                orderedColumns[index].DisplayIndex = index;
            }

            ApplySorting(dataGrid, stateByKey, columnsByKey);
            dataGrid.FrozenColumnCount = Math.Min(state.FrozenColumnCount, dataGrid.Columns.Count);
            return GridLayoutApplyResult.Success(ignored);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return GridLayoutApplyResult.Failure(exception.Message);
        }
    }

    private static GridColumnLayout? CreateLayout(
        DataGridColumn column,
        IReadOnlyDictionary<string, int> sortIndexes,
        int fallbackDisplayIndex)
    {
        var key = GridColumn.GetKey(column);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var fieldPath = GridColumn.GetFieldPath(column)
            ?? ((column as DataGridBoundColumn)?.Binding is Binding binding
                ? binding.Path?.Path
                : null);
        var sortIndex = fieldPath is not null && sortIndexes.TryGetValue(fieldPath, out var index)
            ? index
            : (int?)null;
        return new GridColumnLayout(
            key,
            column.Visibility == Visibility.Visible,
            GetDisplayIndex(column, fallbackDisplayIndex),
            FromDataGridLength(column.Width),
            column.SortDirection,
            sortIndex);
    }

    private static void ApplySorting(
        DataGrid dataGrid,
        IReadOnlyDictionary<string, GridColumnLayout> stateByKey,
        IReadOnlyDictionary<string, DataGridColumn> columnsByKey)
    {
        foreach (var column in dataGrid.Columns)
        {
            column.SortDirection = null;
        }

        var view = CollectionViewSource.GetDefaultView(dataGrid.ItemsSource ?? dataGrid.Items);
        if (!view.CanSort)
        {
            return;
        }

        view.SortDescriptions.Clear();
        foreach (var layout in stateByKey.Values
                     .Where(layout => layout.SortDirection is not null)
                     .OrderBy(layout => layout.SortIndex ?? int.MaxValue))
        {
            if (!columnsByKey.TryGetValue(layout.ColumnKey, out var column))
            {
                continue;
            }

            var fieldPath = GridColumn.GetFieldPath(column)
                ?? ((column as DataGridBoundColumn)?.Binding is Binding binding ? binding.Path?.Path : null);
            if (string.IsNullOrWhiteSpace(fieldPath))
            {
                continue;
            }

            column.SortDirection = layout.SortDirection;
            view.SortDescriptions.Add(new SortDescription(fieldPath, layout.SortDirection!.Value));
        }
    }

    private static GridColumnWidth FromDataGridLength(DataGridLength width) =>
        new(width.Value, width.UnitType switch
        {
            DataGridLengthUnitType.Auto => GridColumnWidthUnit.Auto,
            DataGridLengthUnitType.SizeToCells => GridColumnWidthUnit.SizeToCells,
            DataGridLengthUnitType.SizeToHeader => GridColumnWidthUnit.SizeToHeader,
            DataGridLengthUnitType.Star => GridColumnWidthUnit.Star,
            _ => GridColumnWidthUnit.Pixel
        });

    private static DataGridLength ToDataGridLength(GridColumnWidth width) => width.Unit switch
    {
        GridColumnWidthUnit.Auto => DataGridLength.Auto,
        GridColumnWidthUnit.SizeToCells => DataGridLength.SizeToCells,
        GridColumnWidthUnit.SizeToHeader => DataGridLength.SizeToHeader,
        GridColumnWidthUnit.Star => new DataGridLength(Math.Max(double.Epsilon, width.Value), DataGridLengthUnitType.Star),
        _ => new DataGridLength(Math.Max(0, width.Value), DataGridLengthUnitType.Pixel)
    };

    private static void EnsureUniqueColumnKeys(DataGrid dataGrid)
    {
        var duplicate = dataGrid.Columns
            .Select(GridColumn.GetKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .GroupBy(key => key!, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"DataGrid 中存在重复的 Enterprise Grid ColumnKey：{duplicate}");
        }
    }

    private static int GetDisplayIndex(DataGridColumn column, int fallbackDisplayIndex) =>
        column.DisplayIndex >= 0 ? column.DisplayIndex : fallbackDisplayIndex;
}
