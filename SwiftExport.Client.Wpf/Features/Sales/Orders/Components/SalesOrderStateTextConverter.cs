using System.Globalization;
using System.Windows.Data;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.Sales.Orders.Components;

public sealed class SalesOrderStateTextConverter : IValueConverter
{
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
            _ => value?.ToString() ?? string.Empty,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
