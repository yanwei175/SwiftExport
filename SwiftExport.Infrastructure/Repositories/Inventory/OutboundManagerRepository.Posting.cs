using Dapper;
using System.Data;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Infrastructure.Repositories.Inventory;

/// <summary>出库过账的来源锁、只读预览及状态持久化；不写库存表。</summary>
public sealed partial class OutboundManagerRepository
{

    /// <inheritdoc/>
    public Task<decimal> ReadBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<decimal>(Command("SELECT COALESCE(SUM(OnHandQty),0) FROM dbo.InventoryBalance WHERE WarehouseSiteId=@WarehouseId AND ProductId=@ProductId AND IsDeleted=0", new { WarehouseId = warehouseId, ProductId = productId }, token));

    /// <inheritdoc/>
    public Task<string?> ReadActiveWarehouseNameAsync(Guid warehouseId, CancellationToken token) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<string>(Command("SELECT Name FROM dbo.WarehouseSite WHERE Uid=@WarehouseId AND IsActive=1 AND IsDeleted=0", new { WarehouseId = warehouseId }, token));

    /// <inheritdoc/>
    public async Task MarkPostedAsync(OutboundDocument header, CancellationToken token)
    {
        context.AuditStamp.StampForUpdate(header);
        var count = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.OutboundDocument SET PostingStatus=1,PostedDate=@PostedDate,PostedBy=@PostedBy,
                UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy,Version=Version+1
            WHERE Uid=@Uid AND Version=@Version AND PostingStatus=0 AND LifecycleStatus=0
                AND DocumentType=22 AND TriggerPackingListId IS NULL AND IsDeleted=0;
            """, header, token));
        if (count != 1) throw new DBConcurrencyException("出库状态或版本已变化。");
        header.Version++;
    }
}
