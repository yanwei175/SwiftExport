using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Procurement;
namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Confirmation;

/// <summary>确认依据编排：仅核实采购头行与活动需求引用；不执行库存变动。</summary>
public sealed class ProcurementConfirmationBuilder(IProcurementReferenceReadRepository references)
{
    /// <summary>按来源锁定后检查剩余行活动贡献，确认不能复活已经释放或失效的物料引用。</summary>
    public static void RequireSourceReferences(ProcurementReversalFacts facts)
    {
        var head = facts.Head ?? throw new InvalidOperationException("采购单不存在。");
        foreach (var line in facts.Lines.Where(x => !x.IsDeleted))
        {
            var links = facts.Links.Where(x => x.TargetType == "PO" && x.TargetHeadId == head.Uid && x.TargetLineId == line.Uid
                && !x.IsDeleted && x.LinkStatus == DemandExecutionLinkStatusEnum.Active).ToList();
            // 未关联数量为空表示创建数据缺少数量依据，应与活动 Link 缺失或贡献不一致分别报错。
            if (!line.UnlinkedQty.HasValue)
                throw new InvalidOperationException($"采购第 {line.LineNumber} 行（{line.Sku}）缺少未关联数量依据，请核查创建数据；不能确认。");
            if (line.UnlinkedQty < 0 || line.UnlinkedQty > line.Quantity
                || ((head.OriginKind == 1 || line.SourceUid.HasValue) && links.Count == 0)
                || links.Any(x => x.ReleasedQty < 0 || x.AllocatedQty <= x.ReleasedQty)
                || links.Sum(x => x.AllocatedQty - x.ReleasedQty) + line.UnlinkedQty != line.Quantity
                || links.Any(x => !facts.Demands.Any(n => n.Uid == x.NetDemandId && !n.IsDeleted && n.LifecycleStatus == NetDemandLifecycleStatusEnum.Active
                    && n.DecompositionRevision > 0
                    && facts.Sources.Any(s => s.Uid == n.GrossDemandId && !s.IsDeleted && s.LifecycleStatus == GrossDemandLifecycleStatusEnum.Active
                        && !string.IsNullOrWhiteSpace(s.DemandBasisFingerprint)
                        && !string.IsNullOrWhiteSpace(n.SourceDemandBasisFingerprintSnapshot)
                        && n.SourceDemandBasisFingerprintSnapshot == s.DemandBasisFingerprint))))
                throw new InvalidOperationException($"采购第 {line.LineNumber} 行（{line.Sku}）的活动物料引用缺失、失效或贡献数量不一致，不能确认。");
        }
    }
    /// <summary>检查客户和实际供应商仍存在且启用，名称变化不覆写既有订单快照。</summary>
    public async Task ValidatePartiesAsync(Guid customerId, Guid supplierId, CancellationToken ct)
    {
        var customer = await references.GetCustomerAsync(customerId, ct);
        if (customer is null || !customer.IsActive || string.IsNullOrWhiteSpace(customer.CustomerCode))
            throw new InvalidOperationException("归属客户不存在、停用或缺少编码。");
        _ = await RequireSupplierAsync(supplierId, ct);
    }
    /// <summary>按稳定身份检查供应商资格，默认供应商不作为实际供货关系。</summary>
    public async Task<Supplier> RequireSupplierAsync(Guid uid, CancellationToken ct)
    {
        var supplier = await references.GetSupplierAsync(uid, ct);
        if (supplier is null || !supplier.IsActive || string.IsNullOrWhiteSpace(supplier.SupplierCode) || string.IsNullOrWhiteSpace(supplier.SupplierName))
            throw new InvalidOperationException("供应商不存在、停用或身份资料缺失。");
        return supplier;
    }
    /// <summary>在调用方事务中建立采购事实预览；不读取 BOM 或预先指定库存仓库。</summary>
    public async Task<ProcurementConfirmationContextDto> BuildAsync(ProcurementReversalFacts facts, CancellationToken ct)
    {
        RequireSourceReferences(facts);
        var head = facts.Head!;
        var lines = facts.Lines.Where(x => !x.IsDeleted).ToList();
        ProcurementOrderPolicy.RequireDraft(head, head.Version);
        if (!head.CustomerId.HasValue || lines.Count == 0) throw new InvalidOperationException("采购客户或明细缺失。");
        await ValidatePartiesAsync(head.CustomerId.Value, head.SupplierId, ct);
        var result = new ProcurementConfirmationContextDto { PurchaseOrderId = head.Uid, Version = head.Version };
        foreach (var line in lines.OrderBy(x => x.LineNumber))
        {
            await ValidateLineIdentityAsync(line, head.OriginKind, ct);
            if (!line.ProductId.HasValue)
            {
                // 非标关联由明确的来源非标身份证明，不以空产品或 SKU 前缀自动匹配来源。
                var links = facts.Links.Where(x => x.TargetHeadId==head.Uid && x.TargetLineId==line.Uid
                    && !x.IsDeleted && x.LinkStatus==DemandExecutionLinkStatusEnum.Active).ToArray();
                if (links.Any(link => !facts.Demands.Any(n => n.Uid==link.NetDemandId && n.ProductId==null
                    && facts.Sources.Any(s => s.Uid==n.GrossDemandId && s.IsNonStandard))))
                    throw new InvalidOperationException("非标采购引用不能指向标准物料需求。");
            }
            result.Lines.Add(new ProcurementLineConfirmationContextDto
            {
                PurchaseOrderLineId = line.Uid, LineVersion = line.Version
            });
        }
        result.Fingerprint = ProcurementOrderPolicy.Fingerprint(new
        {
            head.Uid, head.Version,
            Lines = lines.OrderBy(x => x.Uid).ToArray(),
            Links = facts.Links.Where(x => !x.IsDeleted && x.LinkStatus == DemandExecutionLinkStatusEnum.Active).OrderBy(x => x.Uid).ToArray(),
            Demands = facts.Demands.OrderBy(x => x.Uid).ToArray(),
            Sources = facts.Sources.OrderBy(x => x.Uid).ToArray()
        });
        return result;
    }

    /// <summary>标准行核验真实产品，非标行核验创建身份、描述及有效单位；确认与补关联共用且不要求 BOM 或仓库。</summary>
    public async Task ValidateLineIdentityAsync(PurchaseOrderLine line, int originKind, CancellationToken ct)
    {
        ProcurementOrderPolicy.Number(line.Quantity, 4, "采购数量", true);
        if (line.ProductId.HasValue)
        {
            var product = await references.GetProductAsync(line.ProductId.Value, ct);
            ProcurementOrderPolicy.RequireProduct(product);
            if (product!.MaterialType != line.MaterialType || product.SupplyType != line.SupplyType)
                throw new InvalidOperationException("产品物料角色或供货方式已经改变，不能确认身份快照不一致的采购行。");
            var unit = await references.GetUnitAsync(product.Unit?.Trim() ?? "", ct);
            if (string.IsNullOrWhiteSpace(unit) || unit != line.UnitSnapshot)
                throw new InvalidOperationException("基础单位失效或已改变，不能确认历史单位不一致的采购行。");
        }
        else
        {
            if (originKind is not (1 or 2) || string.IsNullOrWhiteSpace(line.Sku)
                || string.IsNullOrWhiteSpace(line.DescriptionZh) || !string.IsNullOrWhiteSpace(line.MaterialType)
                || (!string.IsNullOrWhiteSpace(line.SupplyType) && line.SupplyType != SwiftExport.Contracts.SysEnums.StaticEnums.SupplyTypeEnums.采购)
                || line.DeliveryTargetSupplierId.HasValue)
                throw new InvalidOperationException("非标采购身份或描述不完整，不能把损坏的标准采购自动视为非标。");
            var unit = await references.GetUnitAsync(line.UnitSnapshot?.Trim() ?? "", ct);
            if (string.IsNullOrWhiteSpace(unit) || unit != line.UnitSnapshot)
                throw new InvalidOperationException("非标采购必须具有明确且有效的单位。");
        }
        // 客供件继续要求实际送达供应商；收货仓由入库决定。
        if (line.MaterialType == MaterialTypesEnum.客供件)
        {
            if (!line.DeliveryTargetSupplierId.HasValue)
                throw new InvalidOperationException("客供件缺少送达供应商，不能确认。");
            _ = await RequireSupplierAsync(line.DeliveryTargetSupplierId.Value, ct);
        }
    }
}
