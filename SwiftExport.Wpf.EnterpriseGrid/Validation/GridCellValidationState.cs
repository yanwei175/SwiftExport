using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SwiftExport.Wpf.EnterpriseGrid.Validation;

/// <summary>不参与编辑提交事务的单元格错误呈现状态。</summary>
public sealed record GridCellValidationState(bool HasError, string? Message)
{
    /// <summary>无错误的共享状态。</summary>
    public static GridCellValidationState Empty { get; } = new(false, null);
}

/// <summary>
/// 根据数据项、ColumnKey 和 GridValidation.Errors 计算单元格软错误，不持有可视化 Cell。
/// </summary>
public sealed class GridCellValidationStateConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is null || values[0] == DependencyProperty.UnsetValue)
        {
            return GridCellValidationState.Empty;
        }

        var isRowState = values.Length == 2;
        var columnKey = isRowState ? null : values[1] as string;
        var errorsValue = isRowState ? values[1] : values[2];
        if (errorsValue is not IReadOnlyList<GridValidationError> errors ||
            !isRowState && string.IsNullOrWhiteSpace(columnKey))
        {
            return GridCellValidationState.Empty;
        }

        var messages = errors
            .Where(error => ReferenceEquals(error.Item, values[0]) &&
                            (isRowState || string.Equals(error.ColumnKey, columnKey, StringComparison.Ordinal)))
            .Select(error => error.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.CurrentCulture)
            .ToArray();
        return messages.Length == 0
            ? GridCellValidationState.Empty
            : new GridCellValidationState(true, string.Join(Environment.NewLine, messages));
    }

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
