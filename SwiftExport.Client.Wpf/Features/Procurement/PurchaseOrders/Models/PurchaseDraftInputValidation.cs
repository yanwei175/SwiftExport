namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>采购候选与准备快照共用的本地数值校验；只返回提示，不修改输入或替代服务端复核。</summary>
internal static class PurchaseDraftInputValidation
{
    private const decimal MaximumNumber = 99999999999999.9999m;

    /// <summary>采购数量必须为四位范围内正数，且不能超出当前可创建量。</summary>
    public static string? ValidateQuantity(decimal quantity, decimal remaining)
    {
        if (quantity <= 0) return "采购数量必须大于 0。";
        if (!HasValidPrecisionAndRange(quantity)) return "采购数量超出允许范围或精度，最多四位小数。";
        return quantity > remaining ? $"采购数量超过当前可创建数量 {remaining:N4}，请修改后重新校验。" : null;
    }

    /// <summary>数量和两价按四位数值边界校验；零价合法，金额按服务端规则舍入后仍须处于允许范围。</summary>
    public static string? Validate(decimal quantity, decimal remaining, decimal unitPrice, decimal taxPrice)
    {
        if (ValidateQuantity(quantity, remaining) is { } quantityError) return quantityError;
        if (!HasValidPrecisionAndRange(unitPrice)) return "未税单价必须非负且最多四位小数，不能超出允许范围。";
        if (!HasValidPrecisionAndRange(taxPrice)) return "含税单价必须非负且最多四位小数，不能超出允许范围。";
        try
        {
            var unitAmount = decimal.Round(checked(quantity * unitPrice), 4, MidpointRounding.AwayFromZero);
            var taxAmount = decimal.Round(checked(quantity * taxPrice), 4, MidpointRounding.AwayFromZero);
            if (unitAmount > MaximumNumber || taxAmount > MaximumNumber) return "采购金额超出允许范围，请检查数量与单价。";
        }
        catch (OverflowException) { return "采购金额溢出，请检查数量与单价。"; }
        return null;
    }

    private static bool HasValidPrecisionAndRange(decimal value) => value >= 0 && value <= MaximumNumber && decimal.Round(value, 4) == value;
}
