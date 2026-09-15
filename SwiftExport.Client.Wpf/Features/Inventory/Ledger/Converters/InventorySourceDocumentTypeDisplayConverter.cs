using System.Globalization;
using System.Windows.Data;

namespace SwiftExportClient.Features.Inventory.Ledger.Converters;

/// <summary>将库存流水内部来源类型转换为只读中文展示文本，不改变持久化值。</summary>
public sealed class InventorySourceDocumentTypeDisplayConverter : IValueConverter
{
    /// <summary>显示已知来源、空来源和保留原值的未知来源。</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var sourceType = value?.ToString()?.Trim();
        if (string.IsNullOrEmpty(sourceType)) return "未记录";
        if (string.Equals(sourceType, "InboundDocument", StringComparison.OrdinalIgnoreCase)) return "入库单";
        if (string.Equals(sourceType, "OutboundDocument", StringComparison.OrdinalIgnoreCase)) return "出库单";
        return $"未知来源（{sourceType}）";
    }

    /// <summary>来源类型仅供只读展示，不支持将中文文本写回业务字段。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
