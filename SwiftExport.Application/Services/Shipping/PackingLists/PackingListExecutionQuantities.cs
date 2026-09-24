namespace SwiftExport.Application.Services.Shipping;

/// <summary>箱单执行数量的纯计算规则；不读取当前 BOM、不修改库存或持久化单据。</summary>
public static class PackingListExecutionQuantities
{
    /// <summary>同来源的有效物理箱单行；排序决定确定性的数量归属。</summary>
    public sealed record PhysicalLine(Guid Id, int LineNumber, decimal Qty);

    /// <summary>分配到物理行的数量，可关联一条执行动作或客供件快照。</summary>
    public sealed record Share(Guid LineId, decimal Qty);

    /// <summary>按当前批次出货量计算一次客供件应耗，四位 AwayFromZero，不累计追补历史。</summary>
    public static decimal ComponentConsumption(decimal shippedQty, decimal rootGrossQty, decimal childGrossQty)
    {
        if (shippedQty <= 0 || rootGrossQty <= 0 || childGrossQty < 0)
            throw new ArgumentException("出货量和根需求数量必须大于零，子需求数量不能小于零。");
        RequirePrecision(shippedQty);
        return decimal.Round(checked(shippedQty * childGrossQty) / rootGrossQty, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>将已舍入的客供件总耗用按物理行出货比例拆分，末行吸收尾差，不改变总耗用。</summary>
    public static IReadOnlyList<Share> SplitConsumption(decimal totalConsumption, IReadOnlyList<PhysicalLine> lines)
    {
        RequirePrecision(totalConsumption);
        var ordered = ValidateLines(lines);
        var shipped = ordered.Sum(x => x.Qty);
        var remaining = totalConsumption;
        var result = new List<Share>();
        for (var i = 0; i < ordered.Length; i++)
        {
            // 极小总耗用可能被前面逐行舍入用完，截断到剩余量避免末行出现负值。
            var quantity = i == ordered.Length - 1 ? remaining : Math.Min(remaining,
                decimal.Round(checked(totalConsumption * ordered[i].Qty) / shipped, 4, MidpointRounding.AwayFromZero));
            result.Add(new Share(ordered[i].Id, quantity));
            remaining -= quantity;
        }
        return result;
    }

    /// <summary>按物理行顺序归属一个执行片段；offset 是此前已分配的数量，跨仓拆分不会重复占用行数量。</summary>
    public static IReadOnlyList<Share> AssignShipment(decimal quantity, decimal offset, IReadOnlyList<PhysicalLine> lines)
    {
        RequirePrecision(quantity);
        RequirePrecision(offset);
        var ordered = ValidateLines(lines);
        if (checked(offset + quantity) > ordered.Sum(x => x.Qty))
            throw new ArgumentException("执行数量超过物理行总出货数量。");
        var remaining = quantity;
        var skip = offset;
        var result = new List<Share>();
        foreach (var line in ordered)
        {
            var skipped = Math.Min(skip, line.Qty);
            skip -= skipped;
            var take = Math.Min(remaining, line.Qty - skipped);
            if (take > 0) result.Add(new Share(line.Id, take));
            remaining -= take;
            if (remaining == 0) break;
        }
        return result;
    }

    /// <summary>验证有效物理行身份、正数量和稳定排序，不接受已删除行占位。</summary>
    private static PhysicalLine[] ValidateLines(IReadOnlyList<PhysicalLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || lines.Any(x => x.Id == Guid.Empty || x.LineNumber <= 0 || x.Qty <= 0)
            || lines.Select(x => x.Id).Distinct().Count() != lines.Count)
            throw new ArgumentException("物理行身份必须唯一非空，行号和出货数量必须大于零。");
        foreach (var line in lines) RequirePrecision(line.Qty);
        return lines.OrderBy(x => x.LineNumber).ThenBy(x => x.Id).ToArray();
    }

    /// <summary>执行数量采用非负四位精度，非法输入必须由上层定位而非静默修正。</summary>
    private static void RequirePrecision(decimal quantity)
    {
        if (quantity < 0 || decimal.Round(quantity, 4) != quantity)
            throw new ArgumentException("执行数量必须非负且最多四位小数。");
    }
}
