using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace SwiftExportClient.Resources.Converters;

/// <summary>
/// 比较 Cell 的数据项和列与 DataGrid.CurrentCell，不缓存虚拟化 Cell 容器。
/// </summary>
public sealed class GridCurrentCellStateConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is null || values[1] is not DataGridColumn column ||
            values[2] is not DataGridCellInfo currentCell || !currentCell.IsValid)
        {
            return false;
        }

        return ReferenceEquals(values[0], currentCell.Item) && ReferenceEquals(column, currentCell.Column);
    }

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
