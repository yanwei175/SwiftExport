using Dapper;
using System.Data;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;
/// <summary>读取出库库存执行与反向证明，不写余额或流水。</summary>
public sealed class OutboundExecutionReadRepository(RepositoryBaseContext context) : IOutboundExecutionReadRepository
{
    private CommandDefinition Command(string sql, object? args, CancellationToken token) =>
        new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <inheritdoc/>
    public Task<decimal> ReadBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<decimal>(Command("SELECT COALESCE(SUM(OnHandQty),0) FROM dbo.InventoryBalance WHERE WarehouseSiteId=@WarehouseId AND ProductId=@ProductId AND IsDeleted=0", new { WarehouseId = warehouseId, ProductId = productId }, token));

    /// <inheritdoc/>
    public Task<string?> ReadActiveWarehouseNameAsync(Guid warehouseId, CancellationToken token) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<string>(Command("SELECT Name FROM dbo.WarehouseSite WHERE Uid=@WarehouseId AND IsActive=1 AND IsDeleted=0", new { WarehouseId = warehouseId }, token));

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

}
