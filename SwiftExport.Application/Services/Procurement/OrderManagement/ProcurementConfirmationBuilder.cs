using SwiftExport.Application.Interfaces;
using Mapster;
using SwiftExport.Contracts.DTOs.Products.Execution;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Procurement;
namespace SwiftExport.Application.Services.Procurement.OrderManagement;

/// <summary>确认依据编排：独立采购读取当前 BOM，需求来源剩余行复用历史依据；不执行库存变动。</summary>
public sealed class ProcurementConfirmationBuilder(IProcurementOrderRepository repository, IBomExecutionReader bom,
    IProcurementConfirmationRepository snapshots, TypeAdapterConfig mappings)
{
    /// <summary>按来源锁定后检查剩余行活动贡献，确认不能复活已经释放或失效的物料引用。</summary>
    public static void RequireSourceReferences(ProcurementReversalFacts facts)
    {
        var head = facts.Head ?? throw new InvalidOperationException("采购单不存在。");
        foreach (var line in facts.Lines.Where(x => !x.IsDeleted))
        {
            var links = facts.Links.Where(x => x.TargetType == "PO" && x.TargetHeadId == head.Uid && x.TargetLineId == line.Uid
                && !x.IsDeleted && x.LinkStatus == DemandExecutionLinkStatusEnum.Active).ToList();
            if (head.OriginKind != 1 && !line.SourceUid.HasValue && links.Count == 0) continue;
            if (links.Count == 0 || links.Any(x => x.ReleasedQty < 0 || x.AllocatedQty <= x.ReleasedQty)
                || links.Sum(x => x.AllocatedQty - x.ReleasedQty) + line.UnlinkedQty != line.Quantity
                || links.Any(x => !facts.Demands.Any(n => n.Uid == x.NetDemandId && !n.IsDeleted && n.LifecycleStatus == NetDemandLifecycleStatusEnum.Active
                    && facts.Sources.Any(s => s.Uid == n.GrossDemandId && !s.IsDeleted))))
                throw new InvalidOperationException("采购剩余行的活动物料引用缺失、失效或贡献数量不一致，不能重新确认。");
        }
    }
    /// <summary>检查客户和实际供应商仍存在且启用，名称变化不覆写既有订单快照。</summary>
    public async Task ValidatePartiesAsync(Guid customerId, Guid supplierId, CancellationToken ct)
    {
        var customer = await repository.GetCustomerAsync(customerId, ct);
        if (customer is null || !customer.IsActive || string.IsNullOrWhiteSpace(customer.CustomerCode))
            throw new InvalidOperationException("归属客户不存在、停用或缺少编码。");
        _ = await RequireSupplierAsync(supplierId, ct);
    }
    /// <summary>按稳定身份检查供应商资格，默认供应商不作为实际供货关系。</summary>
    public async Task<Supplier> RequireSupplierAsync(Guid uid, CancellationToken ct)
    {
        var supplier = await repository.GetSupplierAsync(uid, ct);
        if (supplier is null || !supplier.IsActive || string.IsNullOrWhiteSpace(supplier.SupplierCode) || string.IsNullOrWhiteSpace(supplier.SupplierName))
            throw new InvalidOperationException("供应商不存在、停用或身份资料缺失。");
        return supplier;
    }
    /// <summary>当前切片只接受已维护的有效供应商虚拟仓，绝不自动建仓或用 Allocation 推导。</summary>
    public async Task<(WarehouseSite Site, Supplier Supplier)> RequireWarehouseAsync(Guid uid, CancellationToken ct)
    {
        var site = await repository.GetWarehouseAsync(uid, ct);
        if (site is null || !site.IsActive || site.SiteType != WarehouseSiteTypeEnum.SupplierVirtual
            || !site.SupplierId.HasValue || string.IsNullOrWhiteSpace(site.Code) || string.IsNullOrWhiteSpace(site.Name))
            throw new InvalidOperationException("收货或消耗仓库不存在、停用或缺少供应商归属。");
        return (site, await RequireSupplierAsync(site.SupplierId.Value, ct));
    }
    /// <summary>在调用方事务中建立完整上下文；任何无效节点均由严格读取器阻断。</summary>
    public async Task<ProcurementConfirmationContextDto> BuildAsync(PurchaseOrderHead head, List<PurchaseOrderLine> lines, CancellationToken ct)
    {
        ProcurementOrderPolicy.RequireDraft(head, head.Version);
        if (!head.CustomerId.HasValue || lines.Count == 0) throw new InvalidOperationException("采购客户或明细缺失。");
        await ValidatePartiesAsync(head.CustomerId.Value, head.SupplierId, ct);
        var result = new ProcurementConfirmationContextDto { PurchaseOrderId = head.Uid, Version = head.Version };
        var sourceOrder = head.OriginKind == 1 || lines.Any(x => x.SourceUid.HasValue);
        List<PurchaseConfirmationLine> historicalLines = [];
        List<PurchaseConfirmationNode> historicalNodes = [];
        if (sourceOrder)
        {
            var batch = await snapshots.GetLatestBatchAsync(head.Uid, ct)
                ?? throw new InvalidOperationException("需求来源采购缺少历史确认依据；首次确认的收货目标衔接尚未完成，不能用当前 BOM 补造。");
            historicalLines = await snapshots.GetLinesAsync(head.Uid, batch, ct);
            historicalNodes = await snapshots.GetNodesAsync(head.Uid, batch, ct);
        }
        foreach (var line in lines.OrderBy(x => x.LineNumber))
        {
            if (!line.ProductId.HasValue || !line.DeliveryWarehouseSiteId.HasValue
                || (!sourceOrder && (line.UnlinkedQty != line.Quantity || line.SourceUid.HasValue)))
                throw new InvalidOperationException("采购行身份、数量或收货目标不完整。");
            ProcurementOrderPolicy.Number(line.Quantity, 4, "采购数量", true);
            var product = await repository.GetProductAsync(line.ProductId.Value, ct);
            ProcurementOrderPolicy.RequireProduct(product);
            if (product!.MaterialType != line.MaterialType || product.SupplyType != line.SupplyType)
                throw new InvalidOperationException("产品物料角色或供货方式已经改变，不能确认身份快照不一致的采购行。");
            var unit = await repository.GetUnitAsync(product!.Unit?.Trim() ?? "", ct);
            if (string.IsNullOrWhiteSpace(unit) || unit != line.UnitSnapshot)
                throw new InvalidOperationException("基础单位失效或已改变，不能确认历史单位不一致的采购行。");
            _ = await RequireWarehouseAsync(line.DeliveryWarehouseSiteId.Value, ct);
            // 送达供应商是客供件自身的独立目标，不再由收货仓库的供应商归属推导。
            if (line.MaterialType == MaterialTypesEnum.客供件)
            {
                if (!line.DeliveryTargetSupplierId.HasValue)
                    throw new InvalidOperationException("客供件缺少送达供应商，不能确认。");
                _ = await RequireSupplierAsync(line.DeliveryTargetSupplierId.Value, ct);
            }
            result.Lines.Add(new ProcurementLineConfirmationContextDto
            {
                PurchaseOrderLineId = line.Uid, LineVersion = line.Version,
                Bom = sourceOrder ? RestoreBasis(line, historicalLines, historicalNodes) : await bom.ReadAsync(product.Uid, ct)
            });
        }
        result.Fingerprint = ProcurementOrderPolicy.Fingerprint(new
        {
            head.Uid, head.Version,
            Lines = result.Lines.Select(x => new { x.PurchaseOrderLineId, x.LineVersion, x.Bom.Fingerprint }).ToArray()
        });
        return result;
    }

    /// <summary>仅复用相同行及根产品的原始结构；历史缺失或节点损坏时阻断，不读取当前 BOM。</summary>
    private BomExecutionSnapshotDto RestoreBasis(PurchaseOrderLine line, List<PurchaseConfirmationLine> lines, List<PurchaseConfirmationNode> nodes)
    {
        var original = lines.SingleOrDefault(x => x.PurchaseOrderLineId == line.Uid);
        if (original is null || original.RootProductId != line.ProductId || string.IsNullOrWhiteSpace(original.Fingerprint))
            throw new InvalidOperationException("采购剩余行的历史确认依据缺失或产品身份不一致。");
        var children = nodes.Where(x => x.ConfirmationLineId == original.Uid).ToList();
        if (original.HasBom != (children.Count > 0) || children.Any(x => x.IsDeleted || x.RootProductId != original.RootProductId
            || string.IsNullOrWhiteSpace(x.Path) || x.Denominator <= 0 || x.Numerator <= 0)
            || children.Select(x => x.Path).Distinct().Count() != children.Count)
            throw new InvalidOperationException("历史 BOM 节点不完整，不能重新确认。");
        return new BomExecutionSnapshotDto { RootProductId = original.RootProductId, HasBom = original.HasBom,
            Fingerprint = original.Fingerprint, Nodes = children.Select(x => x.Adapt<BomExecutionNodeDto>(mappings)).ToList() };
    }
}
