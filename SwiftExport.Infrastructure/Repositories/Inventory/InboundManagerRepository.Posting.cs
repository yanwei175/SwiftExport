using Dapper;
using System.Data;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Infrastructure.Repositories.Inventory;

/// <summary>入库过账的来源锁、只读预览及状态持久化；不写库存表。</summary>
public sealed partial class InboundManagerRepository
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<InboundPurchasePostingSourceDto>> ReadPostingSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token)
    {
        if (ids.Count == 0) return [];
        return (await context.Session.Connection.QueryAsync<InboundPurchasePostingSourceDto>(Command("""
            SELECT l.Uid AS SourceLineId,l.PurchaseOrderId,l.ProductId,h.OrderNo AS SourceNo,
                l.UnitSnapshot AS Unit,p.Unit AS CurrentUnit,l.QTY AS OriginalQty,
                CONVERT(bit,CASE WHEN h.IsDeleted=0 AND l.IsDeleted=0 AND p.IsDeleted=0
                    AND h.ConfirmationStatus=@Confirmed AND h.LifecycleStatus=@Active
                    AND l.MaterialType IN @KnownTypes THEN 1 ELSE 0 END) AS IsReceivable,
                CONVERT(bit,CASE WHEN l.MaterialType=@CustomerType THEN 1 ELSE 0 END) AS IsCustomerSupplied,
                COALESCE((SELECT SUM(il.Quantity) FROM dbo.InboundDocumentLine il
                    JOIN dbo.InboundDocument ih ON ih.Uid=il.InboundDocumentId
                    WHERE il.SourceLineId=l.Uid AND ih.DocumentType=10 AND ih.PostingStatus IN(1,8)
                        AND il.IsDeleted=0 AND ih.IsDeleted=0),0) AS PostedNetQty
            FROM dbo.PurchaseOrderLine l JOIN dbo.PurchaseOrderHead h ON h.Uid=l.PurchaseOrderId
            LEFT JOIN dbo.Product p ON p.Uid=l.ProductId WHERE l.Uid IN @Ids;
            """, new { Ids = ids, Confirmed = (int)ConfirmationStatusEnum.已确认,
                Active = (int)PurchaseOrderLifecycleStatusEnum.Active, KnownTypes = MaterialTypesEnum.MaterialTypes,
                CustomerType = MaterialTypesEnum.客供件 }, token))).ToList();
    }

    /// <inheritdoc/>
    public async Task LockPurchaseSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token)
    {
        if (ids.Count == 0) return;
        var owners = (await context.Session.Connection.QueryAsync<Guid>(Command(
            "SELECT DISTINCT PurchaseOrderId FROM dbo.PurchaseOrderLine WHERE Uid IN @Ids", new { Ids = ids }, token))).OrderBy(x => x).ToArray();
        // 与采购命令共用物理头行锁，不用入库专属应用锁冒充采购并发保护。
        foreach (var id in owners)
            await context.Session.Connection.QueryAsync<Guid>(Command("SELECT Uid FROM dbo.PurchaseOrderHead WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Id", new { Id = id }, token));
        foreach (var id in ids.Distinct().OrderBy(x => x))
        {
            var owner = await context.Session.Connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                "SELECT PurchaseOrderId FROM dbo.PurchaseOrderLine WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Id", new { Id = id }, token));
            if (owner.HasValue && !owners.Contains(owner.Value)) throw new DBConcurrencyException("采购来源归属已变化，请重新预览。");
        }
    }

    /// <inheritdoc/>
    public Task<bool> HasUnprovenReceiptAsync(CancellationToken token) => context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
        SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.InboundDocumentLine il
            JOIN dbo.InboundDocument ih ON ih.Uid=il.InboundDocumentId
            LEFT JOIN dbo.PurchaseOrderLine pl ON pl.Uid=il.SourceLineId
            WHERE ih.IsDeleted=0 AND il.IsDeleted=0 AND ih.PostingStatus IN(1,8)
                AND (ih.DocumentType IN(11,12) OR (ih.DocumentType=10 AND pl.Uid IS NULL))) THEN 1 ELSE 0 END);
        """, null, token));

    /// <inheritdoc/>
    public Task<decimal> ReadBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<decimal>(Command("SELECT COALESCE(SUM(OnHandQty),0) FROM dbo.InventoryBalance WHERE WarehouseSiteId=@WarehouseId AND ProductId=@ProductId AND IsDeleted=0", new { WarehouseId = warehouseId, ProductId = productId }, token));

    /// <inheritdoc/>
    public Task<string?> ReadActiveWarehouseNameAsync(Guid warehouseId, CancellationToken token) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<string>(Command("SELECT Name FROM dbo.WarehouseSite WHERE Uid=@WarehouseId AND IsActive=1 AND IsDeleted=0", new { WarehouseId = warehouseId }, token));

    /// <inheritdoc/>
    public async Task MarkPostedAsync(InboundDocument header, CancellationToken token)
    {
        context.AuditStamp.StampForUpdate(header);
        var count = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.InboundDocument SET PostingStatus=1,PostedDate=@PostedDate,PostedBy=@PostedBy,
                UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy,Version=Version+1
            WHERE Uid=@Uid AND Version=@Version AND PostingStatus=0 AND LifecycleStatus=0
                AND CreationMode=0 AND TriggerPackingListId IS NULL AND IsDeleted=0;
            """, header, token));
        if (count != 1) throw new DBConcurrencyException("入库状态或版本已变化。");
        header.Version++;
    }
}
