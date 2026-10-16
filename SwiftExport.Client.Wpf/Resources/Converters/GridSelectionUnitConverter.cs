using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace SwiftExportClient.Resources.Converters;

/// <summary>在页面布尔开关与原生 DataGrid.SelectionUnit 之间转换。</summary>
public sealed class GridSelectionUnitConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true
            ? DataGridSelectionUnit.CellOrRowHeader
            : DataGridSelectionUnit.FullRow;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DataGridSelectionUnit.Cell or DataGridSelectionUnit.CellOrRowHeader;
}
