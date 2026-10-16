using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace SwiftExportClient.Resources.Converters;

/// <summary>判断数据项是否为 DataGrid.CurrentCell 所在活动行，不持有行容器。</summary>
public sealed class GridCurrentRowStateConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is null || values[1] is not DataGridCellInfo currentCell)
        {
            return false;
        }

        return currentCell.IsValid && ReferenceEquals(values[0], currentCell.Item);
    }

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
