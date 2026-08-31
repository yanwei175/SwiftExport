using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Procurement;
namespace SwiftExport.Application.Services.Procurement.OrderManagement;

/// <summary>确认依据编排：验证固定身份、收货资格和完整 BOM；不读取需求链，也不执行库存变动。</summary>
public sealed class ProcurementConfirmationBuilder(IProcurementOrderRepository repository, IBomExecutionReader bom)
{
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
        foreach (var line in lines.OrderBy(x => x.LineNumber))
        {
            if (!line.ProductId.HasValue || !line.DeliveryWarehouseSiteId.HasValue || line.UnlinkedQty != line.Quantity
                || line.SourceUid.HasValue || line.DeliveryTargetAllocationId.HasValue)
                throw new InvalidOperationException("独立采购行身份、数量或收货目标不完整。");
            ProcurementOrderPolicy.Number(line.Quantity, 4, "采购数量", true);
            var product = await repository.GetProductAsync(line.ProductId.Value, ct);
            ProcurementOrderPolicy.RequireProduct(product);
            if (product!.MaterialType != line.MaterialType || product.SupplyType != line.SupplyType)
                throw new InvalidOperationException("产品物料角色或供货方式已经改变，不能确认身份快照不一致的采购行。");
            var unit = await repository.GetUnitAsync(product!.Unit?.Trim() ?? "", ct);
            if (string.IsNullOrWhiteSpace(unit) || unit != line.UnitSnapshot)
                throw new InvalidOperationException("基础单位失效或已改变，不能确认历史单位不一致的采购行。");
            var target = await RequireWarehouseAsync(line.DeliveryWarehouseSiteId.Value, ct);
            if (target.Supplier.Uid != line.DeliveryTargetSupplierId)
                throw new InvalidOperationException("收货仓库供应商归属已经改变。");
            result.Lines.Add(new ProcurementLineConfirmationContextDto
            {
                PurchaseOrderLineId = line.Uid, LineVersion = line.Version,
                Bom = await bom.ReadAsync(product.Uid, ct)
            });
        }
        result.Fingerprint = ProcurementOrderPolicy.Fingerprint(new
        {
            head.Uid, head.Version,
            Lines = result.Lines.Select(x => new { x.PurchaseOrderLineId, x.LineVersion, x.Bom.Fingerprint }).ToArray()
        });
        return result;
    }
}
