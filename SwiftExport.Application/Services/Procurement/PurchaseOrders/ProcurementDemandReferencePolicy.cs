using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Models.Procurement;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders;

/// <summary>确认和补关联共用已有需求引用、实物依据及客供关系检查；不读取BOM或比较技术版本快照。</summary>
internal static class ProcurementDemandReferencePolicy
{
    /// <summary>核验本单完整目标、客户、产品、单位、行守恒及物料跨采购承接容量；历史完全释放贡献不占容量。</summary>
    internal static void RequireExisting(ProcurementReversalFacts facts)
    {
        var head=facts.Head??throw new InvalidOperationException("采购单不存在。");
        if(facts.Links.Any(link=>link.TargetType=="PO" && ((link.LinkStatus==DemandExecutionLinkStatusEnum.Active
                && (link.IsDeleted || link.AllocatedQty<=link.ReleasedQty)) || !Enum.IsDefined(link.LinkStatus)
                || link.AllocatedQty<=0 || link.ReleasedQty<0 || link.ReleasedQty>link.AllocatedQty
                || (link.LinkStatus!=DemandExecutionLinkStatusEnum.Active && link.AllocatedQty!=link.ReleasedQty))))
            throw new InvalidOperationException("物料现有采购占用状态或数量异常，请核实后再处理。");
        var lines=facts.Lines.Where(row=>!row.IsDeleted).ToDictionary(row=>row.Uid);
        // 先验证本单完整引用，不能先剔除坏活动Link而把它伪装为纯独立采购。
        var owned=facts.Links.Where(link=>link.TargetHeadId==head.Uid || lines.ContainsKey(link.TargetLineId)).ToArray();
        if(owned.Any(link=>!Enum.IsDefined(link.LinkStatus) || link.AllocatedQty<=0 || link.ReleasedQty<0 || link.ReleasedQty>link.AllocatedQty
            || (link.LinkStatus==DemandExecutionLinkStatusEnum.Active && (link.IsDeleted || link.AllocatedQty<=link.ReleasedQty))
            || ((!link.IsDeleted && link.AllocatedQty>link.ReleasedQty) && (link.TargetType!="PO" || link.TargetHeadId!=head.Uid
                || !lines.ContainsKey(link.TargetLineId) || link.LinkStatus!=DemandExecutionLinkStatusEnum.Active))))
            throw new InvalidOperationException("采购已有引用的活动状态、数量或目标头行不一致，请先核实。");
        var remaining=facts.Links.Where(link=>!link.IsDeleted && link.AllocatedQty>link.ReleasedQty).ToArray();
        if(remaining.Any(link=>(link.TargetHeadId==head.Uid || lines.ContainsKey(link.TargetLineId))
            && (link.TargetType!="PO" || link.TargetHeadId!=head.Uid || !lines.ContainsKey(link.TargetLineId) || link.LinkStatus!=DemandExecutionLinkStatusEnum.Active)))
            throw new InvalidOperationException("采购已有引用的目标头行或活动状态不一致，请先核实。");
        foreach(var line in lines.Values)
        {
            var links=remaining.Where(link=>link.TargetType=="PO" && link.TargetHeadId==head.Uid && link.TargetLineId==line.Uid).ToArray();
            if(!line.UnlinkedQty.HasValue)
                throw new InvalidOperationException($"采购第 {line.LineNumber} 行（{line.Sku}）缺少未关联数量依据，请核查创建数据；不能确认。");
            if(line.Quantity<=0 || line.UnlinkedQty<0 || line.UnlinkedQty>line.Quantity
                || ((head.OriginKind==1 || line.SourceUid.HasValue) && links.Length==0)
                || links.Any(link=>link.ReleasedQty<0 || link.AllocatedQty<=0)
                || links.Sum(link=>link.AllocatedQty-link.ReleasedQty)+line.UnlinkedQty!=line.Quantity)
                throw new InvalidOperationException($"采购第 {line.LineNumber} 行（{line.Sku}）活动贡献与未关联数量不守恒。");
            foreach(var link in links)
            {
                var material=facts.Demands.SingleOrDefault(row=>row.Uid==link.NetDemandId);
                var source=facts.Sources.SingleOrDefault(row=>row.Uid==material?.GrossDemandId);
                if(ReferenceIssue(material,source,head,line) is {} issue)
                    throw new InvalidOperationException($"采购第 {line.LineNumber} 行（{line.Sku}）：{issue}");
                var total=remaining.Where(row=>row.TargetType=="PO" && row.NetDemandId==link.NetDemandId).Sum(row=>row.AllocatedQty-row.ReleasedQty);
                if(total>material!.NetQty)
                    throw new InvalidOperationException($"物料 {material.SkuSnapshot}（{material.Uid}）已有采购承接超过需求数量。");
            }
        }
    }

    /// <summary>按当前实体及非空精确实物指纹检查相容性；请求ExpectedVersion仍由命令入口核验。</summary>
    internal static string? ReferenceIssue(NetDemand? material,GrossDemand? source,PurchaseOrderHead head,PurchaseOrderLine line)
    {
        if(material is null || material.IsDeleted || material.LifecycleStatus!=NetDemandLifecycleStatusEnum.Active || material.DecompositionRevision<=0 || material.NetQty<=0
            || source is null || source.IsDeleted || source.LifecycleStatus!=GrossDemandLifecycleStatusEnum.Active)
            return "物料或成品需求缺失、非活动或数量无效。";
        if(!head.CustomerId.HasValue || head.CustomerId==Guid.Empty || source.CustomerId!=head.CustomerId)
            return "采购与成品需求的客户不一致。";
        if(string.IsNullOrWhiteSpace(source.DemandBasisFingerprint) || string.IsNullOrWhiteSpace(material.SourceDemandBasisFingerprintSnapshot)
            || !string.Equals(material.SourceDemandBasisFingerprintSnapshot,source.DemandBasisFingerprint,StringComparison.Ordinal))
            return "拆解实物依据与当前成品需求不一致。";
        if(string.IsNullOrWhiteSpace(source.SourceType) || source.SourceUid==Guid.Empty)
            return "成品需求来源身份不完整。";
        if(line.ProductId!=material.ProductId || (line.ProductId.HasValue && source.IsNonStandard)
            || (!line.ProductId.HasValue && !source.IsNonStandard) || material.SupplyTypeSnapshot!=SupplyTypeEnums.采购
            || (line.ProductId.HasValue && (line.MaterialType!=material.MaterialTypeSnapshot || line.SupplyType!=material.SupplyTypeSnapshot)))
            return "采购与物料的产品、物料角色或供货方式不一致。";
        if(string.IsNullOrWhiteSpace(line.UnitSnapshot) || string.IsNullOrWhiteSpace(material.UnitSnapshot)
            || !string.Equals(line.UnitSnapshot.Trim(),material.UnitSnapshot.Trim(),StringComparison.OrdinalIgnoreCase))
            return "采购与物料单位缺失或不一致。";
        return null;
    }

    /// <summary>复用已锁定物料及来源锁下读取的目标，检查客供件同树父件和实际送达供应商，不引入仓库条件。</summary>
    internal static void RequireDeliveryRelations(ProcurementReversalFacts facts,IReadOnlyList<ProcurementAssociationTarget> targets,IReadOnlyList<ProcurementDemandAssociationItem> additions)
    {
        var assignments=facts.Links.Where(link=>!link.IsDeleted && link.LinkStatus==DemandExecutionLinkStatusEnum.Active && link.AllocatedQty>link.ReleasedQty)
            .Select(link=>(DemandId:link.NetDemandId,Target:targets.SingleOrDefault(target=>target.LineId==link.TargetLineId && target.HeadId==link.TargetHeadId)
                ??throw new InvalidOperationException("物料已有执行目标缺失或已退出活动状态。")))
            .Concat(additions.Select(item=>(DemandId:item.NetDemandId,Target:new ProcurementAssociationTarget{SupplierId=facts.Head!.SupplierId,
                ProductId=facts.Lines.Single(line=>line.Uid==item.PurchaseOrderLineId).ProductId,
                DeliveryTargetSupplierId=facts.Lines.Single(line=>line.Uid==item.PurchaseOrderLineId).DeliveryTargetSupplierId}))).ToArray();
        foreach(var assignment in assignments)
        {
            var material=facts.Demands.Single(row=>row.Uid==assignment.DemandId);
            if(assignment.Target.ProductId!=material.ProductId)
                throw new InvalidOperationException("物料已有采购引用的产品身份不一致。");
            if(material.MaterialTypeSnapshot!=MaterialTypesEnum.客供件)continue;
            var parent=facts.Demands.SingleOrDefault(row=>row.Uid==material.DeliveryTargetNetDemandId);
            if(parent is null || parent.IsDeleted || parent.LifecycleStatus!=NetDemandLifecycleStatusEnum.Active || parent.GrossDemandId!=material.GrossDemandId
                || parent.RootNetDemandId!=material.RootNetDemandId || parent.DecompositionRevision!=material.DecompositionRevision || material.ParentNetDemandId!=parent.Uid
                || material.DeliveryMode!=DemandDeliveryModeEnum.ParentExecutionSupplier || !assignment.Target.DeliveryTargetSupplierId.HasValue)
                throw new InvalidOperationException("客供件父子需求或送达供应商依据不完整。");
            var suppliers=assignments.Where(row=>row.DemandId==parent.Uid).Select(row=>row.Target.SupplierId).Distinct().ToArray();
            if(suppliers.Length>0 && !suppliers.Contains(assignment.Target.DeliveryTargetSupplierId.Value))
                throw new InvalidOperationException("客供件送达供应商与父件实际采购供应商不相容。");
        }
    }
}
