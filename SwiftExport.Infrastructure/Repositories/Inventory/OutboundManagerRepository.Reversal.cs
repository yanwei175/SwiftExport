using Dapper;
using System.Data;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Infrastructure.Repositories.Inventory;

/// <summary>手工出库反向只读事实核查及单头状态更新；库存写入仍由统一内核执行。</summary>
public sealed partial class OutboundManagerRepository
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<InventoryLedgerEntry>> ReadReverseLedgersAsync(Guid uid, CancellationToken token)
    {
        // 纳入已删除行的身份，暴露异常附加流水；UNION 只去重身份，不掩盖不同流水。
        return (await context.Session.Connection.QueryAsync<InventoryLedgerEntry>(Command("""
            WITH DirectFacts AS (
                SELECT e.Uid FROM dbo.InventoryLedgerEntry e WHERE e.SourceDocumentId=@Uid
                OR EXISTS(SELECT 1 FROM dbo.OutboundDocumentLine l WHERE l.OutboundDocumentId=@Uid
                    AND (l.Uid=e.SourceLineId OR l.CommandItemId=e.CommandItemId))
            ), RelatedFacts AS (
                SELECT Uid FROM DirectFacts UNION
                SELECT e.Uid FROM dbo.InventoryLedgerEntry e WHERE e.ReversalOfLedgerId IN(SELECT Uid FROM DirectFacts)
                    OR e.ReplacementOfLedgerId IN(SELECT Uid FROM DirectFacts)
            )
            SELECT e.Uid,e.CommandId,e.CommandItemId,e.WarehouseSiteId,e.ProductId,e.QuantityDelta,e.UnitSnapshot,
                   e.SkuSnapshot,e.TransactionType,e.SourceDocumentType,e.SourceDocumentId,e.SourceLineId,
                   e.ReversalOfLedgerId,e.ReplacementOfLedgerId,e.CorrectionGroupId,e.IsDeleted
            FROM dbo.InventoryLedgerEntry e JOIN RelatedFacts r ON r.Uid=e.Uid ORDER BY e.Uid;
            """, new { Uid = uid }, token))).ToList();
    }

    /// <inheritdoc/>
    public Task<InventoryBalance?> ReadReverseBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<InventoryBalance>(Command("""
            SELECT Uid,WarehouseSiteId,ProductId,OnHandQty,UnitSnapshot,IsDeleted FROM dbo.InventoryBalance
            WHERE WarehouseSiteId=@WarehouseId AND ProductId=@ProductId;
            """, new { WarehouseId = warehouseId, ProductId = productId }, token));

    /// <inheritdoc/>
    public Task<bool> ReverseWarehouseExistsAsync(Guid warehouseId, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("SELECT CONVERT(bit,COUNT(*)) FROM dbo.WarehouseSite WHERE Uid=@Uid AND IsDeleted=0", new { Uid = warehouseId }, token));

    /// <inheritdoc/>
    public async Task MarkReversedAsync(OutboundDocument header, CancellationToken token)
    {
        context.AuditStamp.StampForUpdate(header);
        var count = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.OutboundDocument SET PostingStatus=8,ReversedDate=@ReversedDate,ReversedBy=@ReversedBy,
                ReverseReason=@ReverseReason,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy,Version=Version+1
            WHERE Uid=@Uid AND Version=@Version AND PostingStatus=1 AND LifecycleStatus=0 AND IsDeleted=0
                AND DocumentType=22 AND TriggerPackingListId IS NULL;
            """, header, token));
        if (count != 1) throw new DBConcurrencyException("出库状态或版本已变化，反过账未完成。");
        header.Version++;
    }
}
