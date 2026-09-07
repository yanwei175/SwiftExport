using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Core.Models.Shipping;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SwiftExport.Application.Services.Shipping;

/// <summary>PackingList 预览纯计算器；输入必须是 Repository 规范化后的事实快照。</summary>
public sealed class PackingListPreviewBuilder
{
    public const string EarlyShipment = "EARLY_SHIPMENT";
    public const string MixedRequiredDates = "MIXED_REQUIRED_DATES";
    public const string ReadyQtyShortfall = "READY_QTY_SHORTFALL";

    public PackingListPreviewDto Build(PackingListPlanningSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var result = new PackingListPreviewDto
        {
            PackingListId = snapshot.Document.Uid,
            Version = snapshot.Document.Version
        };

        var requiredDates = snapshot.Lines
            .Select(x => x.Line.SnapshotRequiredDate?.Date)
            .Where(x => x.HasValue)
            .Distinct()
            .ToList();
        if (requiredDates.Count > 1)
            AddWarning(result, MixedRequiredDates, "本单包含多个需求交期。", true);

        foreach (var fact in snapshot.Lines.OrderBy(x => x.Line.Uid))
        {
            var line = fact.Line;
            var remaining = fact.PlannedQty - fact.ShippedNetQty;
            var sourceAvailable = Math.Max(fact.SourceInboundNetQty - fact.ShippedNetQty, 0m);
            var autoInbound = Math.Max(line.Quantity - sourceAvailable, 0m);
            var previewLine = new PackingListPreviewLineDto
            {
                ContributionId = line.Uid,
                NetDemandAllocationId = line.NetDemandAllocationId,
                Sku = line.SkuSnapshot,
                PackingQty = line.Quantity,
                ShippableRemainingQty = remaining,
                SourceAvailableInboundQty = sourceAvailable,
                AutoInboundQty = autoInbound,
                FinalOutboundQty = line.Quantity
            };
            result.Lines.Add(previewLine);

            if (fact.CustomerId != snapshot.Document.CustomerId)
                AddBlocker(result, "CUSTOMER_MISMATCH", $"{line.SkuSnapshot} 的最终客户与单据客户不一致。");
            if (!fact.IsSourceActive)
                AddBlocker(result, "SOURCE_INACTIVE", $"{line.SkuSnapshot} 的需求来源已失效。");
            if (fact.DemandVersion != line.SourceDemandVersionSnapshot || fact.SourceVersion != line.SourceVersionSnapshot)
                AddBlocker(result, "SOURCE_CHANGED", $"{line.SkuSnapshot} 的来源版本已变化，请重新选择来源。");
            if (!fact.HasOrderPoolOwnership)
                AddBlocker(result, "ORDER_POOL_OWNERSHIP_REQUIRED", $"{line.SkuSnapshot} 缺少可证明的销售订单池归属，不能执行库存动作。");
            if (!fact.IsProductActive)
                AddBlocker(result, "PRODUCT_INACTIVE", $"{line.SkuSnapshot} 产品不存在或已停用。");
            if (!fact.IsWarehouseActive || line.WarehouseSiteId == Guid.Empty)
                AddBlocker(result, "WAREHOUSE_MISSING", $"{line.SkuSnapshot} 的执行供应商虚拟仓不可用。");
            if (line.Quantity <= 0 || line.Quantity > remaining)
                AddBlocker(result, "SHIP_QUANTITY_EXCEEDED", $"{line.SkuSnapshot} 本次数量超过可发货剩余量。");
            if (line.SnapshotRequiredDate.HasValue && snapshot.Document.ShipmentDate.Date < line.SnapshotRequiredDate.Value.Date)
                AddWarning(result, EarlyShipment, $"{line.SkuSnapshot} 早于需求交期发货。", true);
            if (line.Quantity > fact.ReadyQty)
                AddWarning(result, ReadyQtyShortfall, $"{line.SkuSnapshot} 超过采购已备好数量。", true);
            if (!line.NetDemandAllocationId.HasValue && autoInbound > 0)
                AddBlocker(result, "PURCHASE_EXECUTION_REQUIRED", $"{line.SkuSnapshot} 缺少可证明的采购执行依据，不能自动补入库。");

            AddEffect(result, line.WarehouseSiteId, line.WarehouseNameSnapshot, line.ProductId, line.SkuSnapshot,
                fact.BalanceQty, autoInbound, line.Quantity, 0m);

            foreach (var supplied in fact.CustomerSuppliedItems.OrderBy(x => x.AllocationId))
            {
                var lastShipment = line.Quantity >= remaining;
                var target = lastShipment
                    ? supplied.PlannedQty
                    : Round4(supplied.PlannedQty * Math.Min(fact.ShippedNetQty + line.Quantity, fact.PlannedQty) / supplied.ParentPlannedQty);
                var consumption = Math.Max(target - supplied.HistoricalConsumptionQty, 0m);
                previewLine.CustomerSuppliedConsumptionQty += consumption;
                AddEffect(result, line.WarehouseSiteId, line.WarehouseNameSnapshot, supplied.ProductId, supplied.Sku,
                    supplied.BalanceQty, 0m, 0m, consumption);
            }
        }

        // 同仓同产品可能来自多条销售贡献，必须按真实库存键汇总判断，
        // 否则每条都会错误地重复使用同一个 BalanceBefore。
        foreach (var effect in result.InventoryEffects.Where(x => x.BalanceAfter < 0))
        {
            var isCustomerSupplied = effect.ConsumptionQty > 0;
            AddBlocker(result,
                isCustomerSupplied ? "INSUFFICIENT_CUSTOMER_SUPPLIED_STOCK" : "INSUFFICIENT_STOCK",
                isCustomerSupplied ? $"客供件 {effect.Sku} 库存不足。" : $"{effect.Sku} 正常库存不足。");
        }

        result.HasBlockers = result.Blockers.Count > 0;
        result.PreviewToken = CreateToken(snapshot, result);
        return result;
    }

    private static void AddEffect(PackingListPreviewDto result, Guid warehouseId, string? warehouseName, Guid productId,
        string sku, decimal before, decimal inbound, decimal outbound, decimal consumption)
    {
        var effect = result.InventoryEffects.FirstOrDefault(x => x.WarehouseSiteId == warehouseId && x.ProductId == productId);
        if (effect is null)
        {
            effect = new PackingListInventoryEffectDto
            {
                WarehouseSiteId = warehouseId,
                WarehouseName = warehouseName,
                ProductId = productId,
                Sku = sku,
                BalanceBefore = before
            };
            result.InventoryEffects.Add(effect);
        }
        effect.AutoInboundQty += inbound;
        effect.OutboundQty += outbound;
        effect.ConsumptionQty += consumption;
        effect.BalanceAfter = effect.BalanceBefore + effect.AutoInboundQty - effect.OutboundQty - effect.ConsumptionQty;
    }

    private static decimal Round4(decimal value)
        => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static void AddBlocker(PackingListPreviewDto result, string code, string message)
        => result.Blockers.Add(new PackingListMessageDto { Code = code, Message = message });

    private static void AddWarning(PackingListPreviewDto result, string code, string message, bool confirmation)
    {
        if (result.Warnings.Any(x => x.Code == code && x.Message == message)) return;
        result.Warnings.Add(new PackingListMessageDto { Code = code, Message = message, RequiresConfirmation = confirmation });
    }

    private static string CreateToken(PackingListPlanningSnapshot snapshot, PackingListPreviewDto preview)
    {
        var values = new List<string>
        {
            snapshot.Document.Uid.ToString("N"),
            snapshot.Document.Version.ToString(CultureInfo.InvariantCulture),
            snapshot.Document.CustomerId.ToString("N"),
            snapshot.Document.ShipmentDate.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var fact in snapshot.Lines.OrderBy(x => x.Line.Uid))
        {
            values.Add(string.Join(":",
                fact.Line.Uid.ToString("N"), fact.Line.Version, fact.Line.NetDemandAllocationId?.ToString("N") ?? string.Empty,
                DecimalText(fact.Line.Quantity), fact.DemandVersion, fact.SourceVersion, DecimalText(fact.PlannedQty),
                fact.CustomerId.ToString("N"), fact.IsSourceActive, fact.IsProductActive, fact.IsWarehouseActive,
                DecimalText(fact.ShippedNetQty), DecimalText(fact.SourceInboundNetQty), DecimalText(fact.ReadyQty),
                fact.Line.WarehouseSiteId.ToString("N"), fact.BalanceVersion, DecimalText(fact.BalanceQty)));
            foreach (var supplied in fact.CustomerSuppliedItems.OrderBy(x => x.AllocationId))
                values.Add(string.Join(":", supplied.AllocationId.ToString("N"), supplied.ProductId.ToString("N"), supplied.Sku,
                    DecimalText(supplied.PlannedQty),
                    DecimalText(supplied.ParentPlannedQty), DecimalText(supplied.HistoricalConsumptionQty),
                    supplied.BalanceVersion, DecimalText(supplied.BalanceQty)));
        }
        foreach (var line in preview.Lines.OrderBy(x => x.ContributionId))
            values.Add(string.Join(":", line.ContributionId.ToString("N"), DecimalText(line.AutoInboundQty),
                DecimalText(line.FinalOutboundQty), DecimalText(line.CustomerSuppliedConsumptionQty)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", values))));
    }

    private static string DecimalText(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
