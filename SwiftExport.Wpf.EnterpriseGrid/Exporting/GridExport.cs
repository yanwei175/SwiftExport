using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using SwiftExport.Wpf.EnterpriseGrid.Columns;
using SwiftExport.Wpf.EnterpriseGrid.Columns.Enum;
using SwiftExport.Wpf.EnterpriseGrid.Columns.Lookup;
using SwiftExport.Wpf.EnterpriseGrid.Core.Internal;

namespace SwiftExport.Wpf.EnterpriseGrid.Exporting;

/// <summary>从原生 DataGrid 的数据视图和列元数据创建稳定导出快照。</summary>
public static class GridExport
{
    /// <summary>
    /// 创建导出请求。必须在 DataGrid 所属 Dispatcher 线程调用；返回结果不含行/Cell 视觉容器。
    /// </summary>
    public static GridExportRequest CreateRequest(
        DataGrid dataGrid,
        GridExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataGrid);
        dataGrid.Dispatcher.VerifyAccess();
        options ??= new GridExportOptions();

        var columnSnapshots = CreateColumnSnapshots(dataGrid, options).ToArray();
        var rows = new List<GridExportRow>();
        foreach (var item in GetRows(dataGrid, options.RowScope))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(new GridExportRow(columnSnapshots
                .Select(column => CreateCell(item, column, options))
                .ToArray()));
        }

        return new GridExportRequest(
            columnSnapshots.Select(snapshot => snapshot.Descriptor).ToArray(),
            rows.ToArray(),
            options);
    }

    private static IEnumerable<ColumnSnapshot> CreateColumnSnapshots(DataGrid dataGrid, GridExportOptions options)
    {
        foreach (var column in dataGrid.Columns.OrderBy(column => column.DisplayIndex))
        {
            if (!GridColumn.GetCanExport(column) ||
                !options.IncludeHiddenColumns && column.Visibility != System.Windows.Visibility.Visible)
            {
                continue;
            }

            var accessor = GridColumn.GetValueAccessor(column);
            var valueBinding = GetRawValueBinding(column);
            var displayBinding = GetDisplayBinding(column);
            var fieldPath = GridColumn.GetFieldPath(column) ?? GetBindingPath(valueBinding);
            if (accessor is null && string.IsNullOrWhiteSpace(fieldPath))
            {
                if (options.UnboundColumnBehavior == GridUnboundColumnBehavior.Throw)
                {
                    throw new InvalidOperationException($"列“{column.Header}”没有 FieldPath、可推断 Binding.Path 或 GridColumn.ValueAccessor。");
                }

                continue;
            }

            var key = GridColumn.GetKey(column) ?? fieldPath ?? $"column-{column.DisplayIndex}";
            var header = GridColumn.GetExportHeader(column) ?? column.Header?.ToString() ?? key;
            var format = (displayBinding ?? valueBinding)?.StringFormat;
            yield return new ColumnSnapshot(
                column,
                accessor,
                valueBinding,
                displayBinding,
                new GridExportColumn(key, header, fieldPath, column.DisplayIndex, format));
        }
    }

    private static IEnumerable<object> GetRows(DataGrid dataGrid, GridExportRowScope scope)
    {
        IEnumerable source = scope switch
        {
            GridExportRowScope.AllItems => dataGrid.ItemsSource is ICollectionView collectionView
                ? collectionView.SourceCollection
                : dataGrid.ItemsSource ?? dataGrid.Items,
            GridExportRowScope.SelectedItems => dataGrid.Items,
            _ => dataGrid.Items
        };

        HashSet<object>? selected = null;
        if (scope == GridExportRowScope.SelectedItems)
        {
            selected = dataGrid.SelectedItems.Cast<object>().ToHashSet(ReferenceEqualityComparer.Instance);
        }

        return FlattenItems(source)
            .Where(item => IsDataItem(item) && (selected is null || selected.Contains(item!)))
            .Cast<object>();
    }

    private static IEnumerable<object?> FlattenItems(IEnumerable source)
    {
        foreach (var item in source)
        {
            if (item is CollectionViewGroup group)
            {
                foreach (var child in FlattenItems(group.Items))
                {
                    yield return child;
                }
            }
            else
            {
                yield return item;
            }
        }
    }

    private static GridExportCell CreateCell(object item, ColumnSnapshot column, GridExportOptions options)
    {
        var context = new GridExportValueContext(
            column.Descriptor.ColumnKey,
            column.Descriptor.FieldPath,
            options.ValueMode,
            options.Culture);
        var rawValue = column.Accessor is not null
            ? column.Accessor.GetValue(item, context)
            : PropertyPathAccessor.GetValue(item, column.Descriptor.FieldPath);
        var displayValue = options.ValueMode == GridExportValueMode.Display
            ? GetDisplayValue(item, rawValue, column, options.Culture)
            : rawValue;
        return new GridExportCell(rawValue, FormatValue(displayValue, column.Descriptor.Format, options.Culture));
    }

    private static object? GetDisplayValue(
        object item,
        object? rawValue,
        ColumnSnapshot snapshot,
        CultureInfo culture)
    {
        if (snapshot.Column is LookupDataGridColumn lookup)
        {
            if (snapshot.DisplayBinding is { } displayBinding && !string.IsNullOrWhiteSpace(displayBinding.Path?.Path))
            {
                return ApplyBindingPresentation(PropertyPathAccessor.GetValue(item, displayBinding.Path.Path), displayBinding, culture);
            }

            var lookupItem = FindLookupItem(lookup.ItemsSource, rawValue, lookup.SelectedValuePath);
            return PropertyPathAccessor.GetValue(lookupItem, lookup.DisplayMemberPath) ?? rawValue;
        }

        if (snapshot.Column is EnumDataGridColumn enumColumn)
        {
            var enumItem = enumColumn.ItemsSource?.Cast<object>()
                .OfType<EnumDataGridItem>()
                .FirstOrDefault(candidate => Equals(candidate.Value, rawValue));
            return enumItem?.DisplayName ?? rawValue;
        }

        var presentationBinding = snapshot.DisplayBinding ?? snapshot.Binding;
        return presentationBinding is null
            ? rawValue
            : ApplyBindingPresentation(
                snapshot.DisplayBinding is null ? rawValue : PropertyPathAccessor.GetValue(item, snapshot.DisplayBinding.Path?.Path),
                presentationBinding,
                culture);
    }

    private static object? ApplyBindingPresentation(object? value, Binding binding, CultureInfo culture)
    {
        if (binding.Converter is not null)
        {
            value = binding.Converter.Convert(value, typeof(object), binding.ConverterParameter, culture);
        }

        return value;
    }

    private static string FormatValue(object? value, string? format, CultureInfo culture)
    {
        if (value is null || ReferenceEquals(value, System.Windows.DependencyProperty.UnsetValue))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(format))
        {
            return Convert.ToString(value, culture) ?? string.Empty;
        }

        if (format.Contains('{'))
        {
            return string.Format(culture, format, value);
        }

        return value is IFormattable formattable
            ? formattable.ToString(format, culture) ?? string.Empty
            : Convert.ToString(value, culture) ?? string.Empty;
    }

    private static Binding? GetRawValueBinding(DataGridColumn column) => column switch
    {
        LookupDataGridColumn lookup => lookup.SelectedValueBinding as Binding,
        DataGridComboBoxColumn comboBox => comboBox.SelectedValueBinding as Binding
            ?? comboBox.SelectedItemBinding as Binding
            ?? comboBox.TextBinding as Binding,
        DataGridBoundColumn bound => bound.Binding as Binding,
        _ => null
    };

    private static Binding? GetDisplayBinding(DataGridColumn column) =>
        column is LookupDataGridColumn { DisplayBinding: Binding binding } ? binding : null;

    private static string? GetBindingPath(Binding? binding) => binding?.Path?.Path;

    private static object? FindLookupItem(IEnumerable? items, object? selectedValue, string? selectedValuePath)
    {
        if (items is null)
        {
            return null;
        }

        foreach (var item in items)
        {
            if (Equals(PropertyPathAccessor.GetValue(item, selectedValuePath), selectedValue))
            {
                return item;
            }
        }

        return null;
    }

    private static bool IsDataItem(object? item) =>
        item is not null &&
        !ReferenceEquals(item, CollectionView.NewItemPlaceholder) &&
        item is not CollectionViewGroup;

    private sealed record ColumnSnapshot(
        DataGridColumn Column,
        IGridValueAccessor? Accessor,
        Binding? Binding,
        Binding? DisplayBinding,
        GridExportColumn Descriptor);
}
