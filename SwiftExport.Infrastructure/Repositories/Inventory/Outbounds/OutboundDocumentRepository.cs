using Dapper;
using System.Data;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;
/// <summary>保存出库草稿及正式状态，全部写入共用调用方事务。</summary>
public sealed class OutboundDocumentRepository(RepositoryBaseContext context) : IOutboundDocumentRepository
{
    private CommandDefinition Command(string sql, object? args, CancellationToken token) =>
        new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <inheritdoc/>
    public Task<OutboundDocument?> LockHeaderAsync(Guid uid, CancellationToken token) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<OutboundDocument>(Command("SELECT * FROM dbo.OutboundDocument WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Uid AND IsDeleted=0", new { Uid = uid }, token));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboundDocumentLine>> LockLinesAsync(Guid uid, CancellationToken token) =>
        (await context.Session.Connection.QueryAsync<OutboundDocumentLine>(Command("SELECT * FROM dbo.OutboundDocumentLine WITH(UPDLOCK,HOLDLOCK) WHERE OutboundDocumentId=@Uid AND IsDeleted=0 ORDER BY Uid", new { Uid = uid }, token))).ToList();

    /// <inheritdoc/>
    public Task<bool> HasLedgerAsync(Guid uid, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry WHERE SourceDocumentId=@Uid)
            OR EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry e INNER JOIN dbo.OutboundDocumentLine l ON l.Uid=e.SourceLineId WHERE l.OutboundDocumentId=@Uid)
            THEN 1 ELSE 0 END);
            """, new { Uid = uid }, token));

    /// <inheritdoc/>
    public async Task SaveAsync(OutboundDocument header, IReadOnlyList<OutboundDocumentLine> lines, int expectedVersion, CancellationToken token)
    {
        if (expectedVersion == 0)
        {
            context.AuditStamp.StampForInsert(header);
            await context.Session.Connection.ExecuteAsync(Command("""
                INSERT dbo.OutboundDocument(Uid,Version,IsDeleted,DocumentNo,DocumentType,LifecycleStatus,PostingStatus,
                    WarehouseSiteId,WarehouseCodeSnapshot,WarehouseNameSnapshot,ShipmentDate,Remark,IssueReason,TriggerPackingListId,CommandId,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy)
                VALUES(@Uid,1,0,@DocumentNo,@DocumentType,@LifecycleStatus,@PostingStatus,
                    @WarehouseSiteId,@WarehouseCodeSnapshot,@WarehouseNameSnapshot,@ShipmentDate,@Remark,@IssueReason,NULL,@CommandId,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy);
                """, header, token));
            header.Version = 1;
        }
        else
        {
            context.AuditStamp.StampForUpdate(header);
            var changed = await context.Session.Connection.ExecuteAsync(Command("""
                UPDATE dbo.OutboundDocument SET WarehouseSiteId=@WarehouseSiteId,WarehouseCodeSnapshot=@WarehouseCodeSnapshot,
                    WarehouseNameSnapshot=@WarehouseNameSnapshot,ShipmentDate=@ShipmentDate,Remark=@Remark,IssueReason=@IssueReason,
                    Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
                WHERE Uid=@Uid AND Version=@Version AND IsDeleted=0 AND DocumentType=22 AND TriggerPackingListId IS NULL AND PostingStatus=0 AND LifecycleStatus=0;
                """, header, token));
            if (changed != 1) throw new System.Data.DBConcurrencyException("出库草稿发生并发冲突。");
            header.Version++;
        }
        // 整单版本已经锁定；先软删省略行，保留稳定明细 Uid，不重建全部明细身份。
        await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.OutboundDocumentLine SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE OutboundDocumentId=@Uid AND IsDeleted=0 AND Uid NOT IN @Ids;
            """, new { header.Uid, header.UpdatedDate, header.UpdatedBy, Ids = lines.Select(x => x.Uid).ToArray() }, token));
        foreach (var line in lines)
        {
            var isNew = line.Version == 0;
            if (isNew) context.AuditStamp.StampForInsert(line); else context.AuditStamp.StampForUpdate(line);
            var count = await context.Session.Connection.ExecuteAsync(Command(isNew ? """
                INSERT dbo.OutboundDocumentLine(Uid,Version,IsDeleted,OutboundDocumentId,LineNumber,ProductId,SkuSnapshot,DescriptionSnapshot,
                    UnitSnapshot,Quantity,CommandItemId,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy)
                VALUES(@Uid,1,0,@OutboundDocumentId,@LineNumber,@ProductId,@SkuSnapshot,@DescriptionSnapshot,
                    @UnitSnapshot,@Quantity,@CommandItemId,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy);
                """ : """
                UPDATE dbo.OutboundDocumentLine SET LineNumber=@LineNumber,Quantity=@Quantity,Version=Version+1,
                    UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
                WHERE Uid=@Uid AND OutboundDocumentId=@OutboundDocumentId AND Version=@Version AND IsDeleted=0;
                """, line, token));
            if (count != 1) throw new System.Data.DBConcurrencyException("出库明细版本已变化，保存失败。");
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(OutboundDocument header, CancellationToken token)
    {
        context.AuditStamp.StampForUpdate(header);
        var count = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.OutboundDocument SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@Uid AND Version=@Version AND IsDeleted=0 AND DocumentType=22 AND TriggerPackingListId IS NULL AND PostingStatus=0 AND LifecycleStatus=0;
            """, header, token));
        if (count != 1) throw new System.Data.DBConcurrencyException("出库草稿删除发生并发冲突。");
        await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.OutboundDocumentLine SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE OutboundDocumentId=@Uid AND IsDeleted=0;
            """, header, token));
        header.Version++;
    }

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
