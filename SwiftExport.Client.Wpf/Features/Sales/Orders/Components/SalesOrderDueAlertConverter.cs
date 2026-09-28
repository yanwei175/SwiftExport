using System.Globalization;
using System.Windows.Data;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;

namespace SwiftExportClient.Features.Sales.Orders.Components;

/// <summary>把服务端按北京时间计算的交期命中行数与最早日期组成列表提示。</summary>
public sealed class SalesOrderDueAlertConverter : IValueConverter
{
    /// <summary>分别展示逾期与临期命中，避免订单同时命中两种快捷视图时隐藏其中一种提醒。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SalesOrderManagerListItemDto item) return string.Empty;
        var alerts = new List<string>();
        if (item.OverdueLineCount > 0)
            alerts.Add($"逾期 {item.OverdueLineCount} 行 · {item.EarliestOverdueDate:yyyy-MM-dd}");
        if (item.DueSoonLineCount > 0)
            alerts.Add($"临期 {item.DueSoonLineCount} 行 · {item.EarliestDueSoonDate:yyyy-MM-dd}");
        if (alerts.Count > 0) return string.Join(" / ", alerts);
        if (item.PendingPastDueLineCount > 0)
            return $"交期已过 {item.PendingPastDueLineCount} 行";
        return item.HasUndatedOpenLine ? "部分行无交期" : string.Empty;
    }

    /// <summary>交期提示仅供展示，不回写到订单。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
