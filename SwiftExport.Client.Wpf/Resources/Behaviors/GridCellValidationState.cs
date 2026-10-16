using System.Windows.Controls;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SwiftExportClient.Resources.Behaviors;

/// <summary>不参与编辑提交事务的单元格错误呈现状态。</summary>
public sealed record GridCellValidationState(bool HasError, string? Message)
{
    /// <summary>无错误的共享状态。</summary>
    public static GridCellValidationState Empty { get; } = new(false, null);
}

/// <summary>
/// 根据数据项、Column 和 GridValidation.Errors 计算单元格软错误，不持有可视化 Cell。
/// </summary>
public sealed class GridCellValidationStateConverter : IMultiValueConverter
{
    /// <summary>按行对象和原生列引用聚合提示；两项输入用于行状态，三项用于单元格状态。</summary>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is null || values[0] == DependencyProperty.UnsetValue)
        {
            return GridCellValidationState.Empty;
        }

        var isRowState = values.Length == 2;
        var column = isRowState ? null : values[1] as DataGridColumn;
        var errorsValue = isRowState ? values[1] : values[2];
        if (errorsValue is not IReadOnlyList<GridValidationError> errors ||
            !isRowState && column is null)
        {
            return GridCellValidationState.Empty;
        }

        var messages = errors
            .Where(error => ReferenceEquals(error.Item, values[0]) &&
                            (isRowState || ReferenceEquals(error.Column, column)))
            .Select(error => error.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.CurrentCulture)
            .ToArray();
        return messages.Length == 0
            ? GridCellValidationState.Empty
            : new GridCellValidationState(true, string.Join(Environment.NewLine, messages));
    }

    /// <summary>提示为只读计算结果，不支持向模型反写。</summary>
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
