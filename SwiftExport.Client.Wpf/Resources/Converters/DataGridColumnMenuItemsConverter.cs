using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SwiftExportClient.Resources.Converters;

/// <summary>仅在菜单打开时投影有文字标题的原生列，关闭后释放列引用；不维护列状态副本。</summary>
public sealed class DataGridColumnMenuItemsConverter : IMultiValueConverter
{
    /// <summary>按当前显示顺序返回菜单列，未打开时返回空集合。</summary>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [IEnumerable<DataGridColumn> columns, true]
            ? columns.Where(column => column.Header is string text && !string.IsNullOrWhiteSpace(text))
                .OrderBy(column => column.DisplayIndex).ToArray()
            : Array.Empty<DataGridColumn>();

    /// <summary>菜单投影不回写列集合。</summary>
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
