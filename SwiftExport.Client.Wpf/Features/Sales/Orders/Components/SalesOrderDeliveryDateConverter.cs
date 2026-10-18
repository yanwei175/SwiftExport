using System.Globalization;
using System.Windows.Data;

namespace SwiftExportClient.Features.Sales.Orders.Components;

/// <summary>展示行指定交期或当前订单头交期；只解释显示值，不向订单行固化继承日期。</summary>
public sealed class SalesOrderDeliveryDateConverter : IMultiValueConverter
{
    /// <summary>第一项为行交期，未指定时回退到第二项订单头交期；均为空时保持空白。</summary>
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.ElementAtOrDefault(0) is DateTime line ? line : values.ElementAtOrDefault(1) is DateTime header ? header : null;

    /// <summary>继承日期仅供展示，实际行编辑仍写入自己的可空日期字段。</summary>
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
