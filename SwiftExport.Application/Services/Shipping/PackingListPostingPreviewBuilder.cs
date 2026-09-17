using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Core.Entities.Shipping;

namespace SwiftExport.Application.Services.Shipping;

/// <summary>使用已解析的规范化事实生成只读过账方案；不取号、不预留库存、不调用入出库端口。</summary>
public sealed class PackingListPostingPreviewBuilder
{
    /// <summary>来源与当前拆解结果；无库存来源的根需求及采购候选可以为空。</summary>
    public sealed record Source(Guid Id, Guid CustomerId, Guid? ProductId, string Unit, bool IsActive,
        bool DeliveryOnly, decimal RemainingQty, Guid? RootId, decimal RootQty, int Version,
        IReadOnlyList<Guid> PurchaseLineIds, IReadOnlyList<Guid> PreferredWarehouses,
        IReadOnlyList<Component> Components);

    /// <summary>客供件拆解快照；不要求采购关系。</summary>
    public sealed record Component(Guid Id, Guid ProductId, string Unit, decimal GrossQty, int Version);

    /// <summary>有效仓库中的库存事实；Id 是仓库产品键的稳定派生身份，缺失余额按零容量传入。</summary>
    public sealed record Stock(Guid Id, Guid WarehouseId, string WarehouseCode, Guid ProductId,
        string Unit, decimal Qty, int Version);

    /// <summary>允许新增收货的采购行事实；不可收货的关联行不加入此集合。</summary>
    public sealed record Receipt(Guid Id, Guid WarehouseId, string WarehouseCode, Guid ProductId,
        string Unit, decimal RemainingQty, string OrderNo, int LineNumber, int Version);

    /// <summary>明确阻断及对应来源，可显示在预览窗口而非伪造成功方案。</summary>
    public sealed record Blocker(string Code, Guid? GrossDemandId, string Message, decimal? Shortfall = null);

    /// <summary>最终动作，类型为 Delivery、Receipt、Shipment 或 Consumption；数量映射到物理箱单行。</summary>
    public sealed record Action(string Kind, Guid PackingListLineId, Guid GrossDemandId, Guid? ProductId,
        string Unit, Guid? WarehouseId, Guid? PurchaseLineId, Guid? RootId, Guid? ComponentId,
        decimal RootQty, decimal ComponentQty, decimal ShippedQty, decimal Qty);

    /// <summary>只有无阻断时提供令牌与完整方案；事实版本变更会改变令牌。</summary>
    public sealed record Preview(string? Token, IReadOnlyList<Action> Actions, IReadOnlyList<Blocker> Blockers);

    /// <summary>验证交付额度，构建共享容量图，并将方案转换为可追溯的物理行动作。</summary>
    public Preview Build(PackingListHead head, IReadOnlyList<PackingListLine> lines,
        IReadOnlyList<Source> sources, IReadOnlyList<Stock> stocks, IReadOnlyList<Receipt> receipts,
        DateTime businessDate, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var blockers = new List<Blocker>();
        var actions = new List<Action>();
        var active = lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineNumber).ThenBy(x => x.Uid).ToArray();
        if (head.IsDeleted || head.PostingStatus != PackingListPostingStatusEnum.Unposted
            || head.LifecycleStatus != PackingListLifecycleStatusEnum.Active)
            blockers.Add(new("INVALID_STATE", null, "只有有效未过账草稿可以过账。"));
        if (active.Length == 0 || active.Any(x => x.PackingListHeadId != head.Uid || x.Qty <= 0
            || decimal.Round(x.Qty, 4) != x.Qty || x.LineNumber <= 0)
            || active.Select(x => x.Uid).Distinct().Count() != active.Length)
            blockers.Add(new("INVALID_LINES", null, "箱单行归属、身份或数量无效。"));
        if (sources.GroupBy(x => x.Id).Any(x => x.Count() != 1))
            blockers.Add(new("AMBIGUOUS_SOURCE", null, "来源事实存在重复身份。"));
        if (blockers.Count != 0) return new(null, [], blockers);
        var sourceMap = sources.ToDictionary(x => x.Id);
        var demands = new List<PackingListCapacityAllocator.Demand>();
        var candidates = new List<PackingListCapacityAllocator.Candidate>();
        var owners = new Dictionary<Guid, (Source Source, Component? Component, decimal Qty, PackingListLine[] Lines)>();
        foreach (var group in active.GroupBy(x => x.GrossDemandId))
        {
            token.ThrowIfCancellationRequested();
            var qty = group.Sum(x => x.Qty);
            if (!sourceMap.TryGetValue(group.Key, out var source) || !source.IsActive)
            {
                blockers.Add(new("SOURCE_INVALID", group.Key, "来源不存在或已失效。"));
                continue;
            }
            if (source.CustomerId != head.CustomerId)
                blockers.Add(new("CUSTOMER_MISMATCH", source.Id, "来源不属于箱单客户。"));
            if (qty > source.RemainingQty)
                blockers.Add(new("DELIVERY_EXCEEDED", source.Id, "本次出货超过待交付数量。", qty - source.RemainingQty));
            if (group.Any(x => x.ProductId != source.ProductId || !SameUnit(x.UnitSnapshot, source.Unit)))
                blockers.Add(new("SOURCE_CHANGED", source.Id, "产品或单位与当前来源不一致，请更新草稿。"));
            if (source.DeliveryOnly)
            {
                actions.AddRange(group.Select(x => new Action("Delivery", x.Uid, source.Id, source.ProductId,
                    source.Unit, null, null, null, null, 0, 0, qty, x.Qty)));
                continue;
            }
            if (source.ProductId is null || source.RootId is null || source.RootQty <= 0
                || string.IsNullOrWhiteSpace(source.Unit) || source.PurchaseLineIds.Count == 0)
            {
                blockers.Add(new("DEMAND_INVALID", source.Id, "库存来源缺少有效产品、单位、根需求或采购执行关系。"));
                continue;
            }
            AddDemand(source.Id, source.ProductId.Value, source.Unit, qty, null);
            foreach (var component in source.Components.OrderBy(x => x.Id))
            {
                if (component.Id == Guid.Empty || component.ProductId == Guid.Empty || component.GrossQty < 0
                    || string.IsNullOrWhiteSpace(component.Unit))
                {
                    blockers.Add(new("COMPONENT_INVALID", source.Id, "客供件拆解快照无效。"));
                    continue;
                }
                var consumed = PackingListExecutionQuantities.ComponentConsumption(qty, source.RootQty, component.GrossQty);
                if (consumed > 0) AddDemand(component.Id, component.ProductId, component.Unit, consumed, component);
            }

            // 每个需求使用同一套供应节点；库存和采购容量不能按来源复制。
            void AddDemand(Guid id, Guid product, string unit, decimal amount, Component? component)
            {
                if (!owners.TryAdd(id, (source, component, qty, group.ToArray())))
                {
                    blockers.Add(new("DUPLICATE_DEMAND", source.Id, "需求身份重复，不能重复耗用。"));
                    return;
                }
                demands.Add(new(id, amount, component != null));
                var stockCandidates = stocks.Where(x => x.ProductId == product && SameUnit(x.Unit, unit))
                    .OrderBy(x => source.PreferredWarehouses.Contains(x.WarehouseId) ? 0 : 1)
                    .ThenBy(x => x.WarehouseCode, StringComparer.Ordinal).ThenBy(x => x.WarehouseId).ToArray();
                for (var i = 0; i < stockCandidates.Length; i++) candidates.Add(new(stockCandidates[i].Id, id, i));
                if (component != null) return;
                var receiptCandidates = receipts.Where(x => source.PurchaseLineIds.Contains(x.Id)
                    && x.ProductId == product && SameUnit(x.Unit, unit))
                    .OrderBy(x => x.OrderNo, StringComparer.Ordinal).ThenBy(x => x.LineNumber).ThenBy(x => x.Id).ToArray();
                for (var i = 0; i < receiptCandidates.Length; i++) candidates.Add(new(receiptCandidates[i].Id, id, i));
            }
        }
        if (blockers.Count != 0) return new(null, [], blockers);
        // 无库存交付不应受无关仓库或采购容量影响，也不进入库存分配器。
        var usedSupplyIds = candidates.Select(x => x.SupplyId).ToHashSet();
        var supplies = stocks.Where(x => usedSupplyIds.Contains(x.Id))
            .Select(x => new PackingListCapacityAllocator.Supply(x.Id, Math.Max(0, x.Qty), false))
            .Concat(receipts.Where(x => usedSupplyIds.Contains(x.Id))
                .Select(x => new PackingListCapacityAllocator.Supply(x.Id, x.RemainingQty, true))).ToArray();
        var allocation = new PackingListCapacityAllocator().Allocate(supplies, demands, candidates, token);
        if (!allocation.Success)
            return new(null, [], allocation.Shortfalls.Select(x => new Blocker(
                owners[x.Key].Component == null ? "SUPPLY_SHORTFALL" : "COMPONENT_SHORTFALL",
                owners[x.Key].Source.Id, "可用库存或关联采购容量不足。", x.Value)).ToArray());
        var stockMap = stocks.Where(x => usedSupplyIds.Contains(x.Id)).ToDictionary(x => x.Id);
        var receiptMap = receipts.Where(x => usedSupplyIds.Contains(x.Id)).ToDictionary(x => x.Id);
        foreach (var group in allocation.Allocations.GroupBy(x => x.DemandId).OrderBy(x => x.Key))
        {
            var owner = owners[group.Key];
            var physical = owner.Lines.Select(x => new PackingListExecutionQuantities.PhysicalLine(x.Uid, x.LineNumber, x.Qty)).ToArray();
            // 客供件总量先归属到物理行，再切分仓库动作，避免每仓独立舍入。
            if (owner.Component != null)
            {
                var shares = PackingListExecutionQuantities.SplitConsumption(group.Sum(x => x.Quantity), physical);
                physical = shares.Where(x => x.Qty > 0).Select((x, i) => new PackingListExecutionQuantities.PhysicalLine(x.LineId, i + 1, x.Qty)).ToArray();
            }
            decimal offset = 0;
            foreach (var item in group.OrderBy(x => x.SupplyId))
            {
                var receipt = receiptMap.GetValueOrDefault(item.SupplyId);
                var warehouse = receipt?.WarehouseId ?? stockMap[item.SupplyId].WarehouseId;
                foreach (var share in PackingListExecutionQuantities.AssignShipment(item.Quantity, offset, physical))
                {
                    var action = new Action(owner.Component == null ? "Shipment" : "Consumption", share.LineId,
                        owner.Source.Id, owner.Component?.ProductId ?? owner.Source.ProductId,
                        owner.Component?.Unit ?? owner.Source.Unit, warehouse, null, owner.Source.RootId,
                        owner.Component?.Id, owner.Source.RootQty, owner.Component?.GrossQty ?? 0, owner.Qty, share.Qty);
                    if (receipt != null) actions.Add(action with { Kind = "Receipt", PurchaseLineId = receipt.Id });
                    actions.Add(action);
                }
                offset += item.Quantity;
            }
        }
        // 序列化排序后的事实而非客户端提交的方案；包含版本及所有影响分配的容量。
        var fingerprint = JsonSerializer.Serialize(new { Rule = 1, Head = head, Lines = active, Date = businessDate.Date,
            Sources = sources.OrderBy(x => x.Id).Select(x => x with { PurchaseLineIds = x.PurchaseLineIds.Order().ToArray(),
                PreferredWarehouses = x.PreferredWarehouses.Order().ToArray(), Components = x.Components.OrderBy(c => c.Id).ToArray() }),
            Stocks = stocks.OrderBy(x => x.Id), Receipts = receipts.OrderBy(x => x.Id), Actions = actions });
        return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint))), actions, []);
    }

    /// <summary>仅规范空白及大小写，不做单位换算或猜测。</summary>
    private static bool SameUnit(string? first, string? second) =>
        string.Equals(first?.Trim() ?? "", second?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);
}
