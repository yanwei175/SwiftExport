using System.Globalization;
using System.Windows.Data;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.Sales.Orders.Components;

/// <summary>把销售订单状态和行类型转换为页面可读文本，并将缺失的交付状态明确显示为未知。</summary>
public sealed class SalesOrderStateTextConverter : IValueConverter
{
    /// <summary>转换销售订单状态、履约状态或标准/非标标记。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            SalesOrderDocumentStatusEnum.Open => "正常",
            SalesOrderDocumentStatusEnum.Closed => "已关闭",
            SalesOrderFulfillmentStatusEnum.NotStarted => "未发货",
            SalesOrderFulfillmentStatusEnum.PartiallyShipped => "部分发货",
            SalesOrderFulfillmentStatusEnum.FullyShipped => "全部发货",
            true => "非标",
            false => "标准",
            _ => value?.ToString() ?? "未知",
        };
    }

    /// <summary>状态文本只读展示，不执行反向转换。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
