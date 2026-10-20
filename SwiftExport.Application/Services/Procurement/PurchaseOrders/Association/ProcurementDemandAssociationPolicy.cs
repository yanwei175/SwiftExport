using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Models.Procurement;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Association;

/// <summary>采购补关联的纯规则：允许活动已确认订单，守恒按采购行和物料汇总，收货与出货不作统一阻断。</summary>
public static class ProcurementDemandAssociationPolicy
{
    /// <summary>校验稳定身份、版本、精度和唯一贡献，避免重复行被静默合并。</summary>
    public static void ValidateInput(Guid uid, int version, IReadOnlyList<ProcurementDemandAssociationItem>? items)
    {
        if (uid == Guid.Empty || version <= 0 || items is null || items.Count is < 1 or > 500
            || items.Any(x => x is null || x.CommandItemId == Guid.Empty || x.PurchaseOrderLineId == Guid.Empty
                || x.NetDemandId == Guid.Empty || x.ExpectedLineVersion <= 0 || x.ExpectedDemandVersion <= 0 || x.ExpectedSourceDemandVersion <= 0)
            || items.Select(x => x.CommandItemId).Distinct().Count() != items.Count
            || items.Select(x => (x.PurchaseOrderLineId,x.NetDemandId)).Distinct().Count() != items.Count)
            throw ProcurementOperationException.Validation("关联须提交1至500条具有独立身份、版本的唯一贡献。", "Items");
        foreach (var item in items) ProcurementOrderPolicy.Number(item.Quantity, 4, "Items[].Quantity", true);
    }

    /// <summary>采购必须活动且身份完整，确认状态不改变补关联资格。</summary>
    public static PurchaseOrderHead RequireHead(ProcurementReversalFacts facts)
    {
        var head = facts.Head ?? throw new ProcurementOperationException(ServiceErrorCodes.NotFound, "采购单不存在。");
        if (head.IsDeleted || head.LifecycleStatus != PurchaseOrderLifecycleStatusEnum.Active
            || head.ConfirmationStatus is not (ConfirmationStatusEnum.待确认 or ConfirmationStatusEnum.已确认)
            || !head.CustomerId.HasValue || head.CustomerId == Guid.Empty || head.SupplierId == Guid.Empty
            || head.OriginKind is not (1 or 2))
            Block("采购已退出活动状态或创建身份、客户、供应商不明确，不能追加关联。");
        return head;
    }

    /// <summary>生成分配预览；全部现有引用以及本次选择共同证明唯一来源与数量守恒。</summary>
    public static ProcurementDemandAssociationPreviewDto Build(ProcurementReversalFacts facts,
        IReadOnlyList<ProcurementAssociationTarget> targets, int expectedVersion, IReadOnlyList<ProcurementDemandAssociationItem> items)
    {
        var head = RequireHead(facts);
        if (head.Version != expectedVersion) Conflict("采购版本已变化，请刷新。");
        ProcurementConfirmationBuilder.RequireSourceReferences(facts);
        var lines = facts.Lines.Where(x => !x.IsDeleted).ToDictionary(x => x.Uid);
        var demands = facts.Demands.ToDictionary(x => x.Uid);
        var sources = facts.Sources.ToDictionary(x => x.Uid);
        var active = facts.Links.Where(x => x.TargetType=="PO" && (x.LinkStatus==DemandExecutionLinkStatusEnum.Active
            || x.AllocatedQty!=x.ReleasedQty || x.AllocatedQty<=0 || x.ReleasedQty<0 || !Enum.IsDefined(x.LinkStatus))).ToArray();
        // 未释放却标记取消、执行目标失效等异常不作为空余量；拒绝而非允许重复承接。
        foreach (var link in active)
        {
            if (link.IsDeleted || link.LinkStatus != DemandExecutionLinkStatusEnum.Active || link.AllocatedQty<=link.ReleasedQty || link.ReleasedQty < 0 || link.TargetType != "PO"
                || !targets.Any(t => t.LineId == link.TargetLineId && t.HeadId == link.TargetHeadId)
                || !demands.TryGetValue(link.NetDemandId, out var linkedDemand))
                Block("物料现有执行贡献或采购目标异常，请先核实引用。");
            if (targets.Single(t => t.LineId == link.TargetLineId).ProductId != demands[link.NetDemandId].ProductId)
                Block("物料已有采购引用的产品身份不一致。");
        }
        var selected = new List<(ProcurementDemandAssociationItem Item, PurchaseOrderLine Line, NetDemand Demand, GrossDemand Source)>();
        foreach (var item in items)
        {
            if (!lines.TryGetValue(item.PurchaseOrderLineId, out var line)
                || !demands.TryGetValue(item.NetDemandId, out var demand)
                || !sources.TryGetValue(demand.GrossDemandId, out var source))
                throw ProcurementOperationException.Validation("选定采购行或物料需求不存在、已退出，或归属已变化。", "Items");
            if (line.Version != item.ExpectedLineVersion || demand.Version != item.ExpectedDemandVersion || source.Version != item.ExpectedSourceDemandVersion)
                Conflict("采购行、物料或来源版本已变化，请重新预览。");
            if(ProcurementDemandReferencePolicy.ReferenceIssue(demand,source,head,line) is {} issue)Block(issue);
            // 来源仅推进技术版本且实物依据不变时，原拆解仍有效；请求版本防竞态，依据指纹证明实物相容。
            if (!line.ProductId.HasValue && (!source.IsNonStandard || !item.AcceptNonStandardMatch))
                Block("非标采购必须明确人工确认与非标来源的匹配关系。");
            selected.Add((item,line,demand,source));
        }
        var ownSources = active.Where(x => x.TargetHeadId == head.Uid)
            .Select(x => sources[demands[x.NetDemandId].GrossDemandId]);
        var sourceKeys = ownSources.Concat(selected.Select(x => x.Source)).Select(x => (x.SourceType,x.SourceUid)).Distinct().ToArray();
        if (sourceKeys.Length != 1 || (head.DemandSourceUid.HasValue && (head.DemandSourceUid != sourceKeys[0].SourceUid || head.DemandSourceType != sourceKeys[0].SourceType))
            || lines.Values.Any(x => x.SourceUid.HasValue && (x.SourceUid != sourceKeys[0].SourceUid || x.SourceType != sourceKeys[0].SourceType)))
            Block("一张采购单只允许关联同一类型、同一来源单据。");
        var lineTotals = selected.GroupBy(x => x.Line.Uid).ToDictionary(x => x.Key, x => x.Sum(y => y.Item.Quantity));
        var demandTotals = selected.GroupBy(x => x.Demand.Uid).ToDictionary(x => x.Key, x => x.Sum(y => y.Item.Quantity));
        foreach (var row in selected)
        {
            if (!row.Line.UnlinkedQty.HasValue || row.Line.UnlinkedQty < lineTotals[row.Line.Uid])
                Block("本次分配超过采购行剩余未关联量。");
            if (row.Demand.NetQty <= 0 || row.Demand.NetQty - active.Where(x => x.NetDemandId == row.Demand.Uid).Sum(x => x.AllocatedQty-x.ReleasedQty) < demandTotals[row.Demand.Uid])
                Block("本次分配超过物料需求剩余可承接量。");
        }
        try{ProcurementDemandReferencePolicy.RequireDeliveryRelations(facts,targets,selected.Select(x=>x.Item).ToArray());}
        catch(InvalidOperationException ex){Block(ex.Message);}
        return new()
        {
            PurchaseOrderId = head.Uid, Version = head.Version,
            Token = ProcurementOrderPolicy.Fingerprint(new
            {
                head, Lines = facts.Lines.OrderBy(x => x.Uid), Sources = facts.Sources.OrderBy(x => x.Uid),
                Demands = facts.Demands.OrderBy(x => x.Uid), Links = facts.Links.OrderBy(x => x.Uid),
                Targets = targets.OrderBy(x => x.LineId), Items = items.OrderBy(x => x.CommandItemId)
            }),
            Items = selected.OrderBy(x => x.Item.CommandItemId).Select(x => new ProcurementDemandAssociationPreviewItemDto
            {
                CommandItemId = x.Item.CommandItemId, PurchaseOrderLineId = x.Line.Uid, LineNumber = x.Line.LineNumber,
                PurchaseSku = x.Line.Sku, NetDemandId = x.Demand.Uid, SourceNo = x.Source.SourceNo,
                DemandSku = x.Demand.SkuSnapshot, Unit = x.Demand.UnitSnapshot!, Quantity = x.Item.Quantity,
                LineUnlinkedBefore = x.Line.UnlinkedQty!.Value, LineUnlinkedAfter = x.Line.UnlinkedQty.Value-lineTotals[x.Line.Uid],
                DemandRemainingBefore = x.Demand.NetQty-active.Where(l => l.NetDemandId==x.Demand.Uid).Sum(l => l.AllocatedQty-l.ReleasedQty),
                DemandRemainingAfter = x.Demand.NetQty-active.Where(l => l.NetDemandId==x.Demand.Uid).Sum(l => l.AllocatedQty-l.ReleasedQty)-demandTotals[x.Demand.Uid]
            }).ToList()
        };
    }

    /// <summary>业务拒绝由采购统一支持层转换，不引入专用异常框架。</summary>
    private static void Block(string message) => throw new ProcurementOperationException(ServiceErrorCodes.BusinessBlocked, message);
    /// <summary>事实版本冲突统一返回 ExpectedVersion，客户端刷新后重新分配。</summary>
    private static void Conflict(string message) => throw new ProcurementOperationException(ServiceErrorCodes.VersionConflict, message, "ExpectedVersion");
}
