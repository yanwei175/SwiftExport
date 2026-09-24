using Dapper;
using System.Data;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

/// <summary>在调用方事务中锁定并写入入库头行及生命周期状态，不写库存事实。</summary>
public sealed class InboundDocumentRepository(RepositoryBaseContext context) : IInboundDocumentRepository
{
    private CommandDefinition Command(string sql, object? args, CancellationToken token) =>
        new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <inheritdoc/>
    public Task<InboundDocument?> LockHeaderAsync(Guid uid, CancellationToken token) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<InboundDocument>(Command("SELECT * FROM dbo.InboundDocument WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Uid AND IsDeleted=0", new { Uid = uid }, token));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InboundDocumentLine>> LockLinesAsync(Guid uid, CancellationToken token) =>
        (await context.Session.Connection.QueryAsync<InboundDocumentLine>(Command("SELECT * FROM dbo.InboundDocumentLine WITH(UPDLOCK,HOLDLOCK) WHERE InboundDocumentId=@Uid AND IsDeleted=0 ORDER BY Uid", new { Uid = uid }, token))).ToList();

    /// <inheritdoc/>
    public async Task SaveAsync(InboundDocument header, IReadOnlyList<InboundDocumentLine> lines, int expectedVersion, CancellationToken token)
    {
        if (expectedVersion == 0)
        {
            context.AuditStamp.StampForInsert(header);
            await context.Session.Connection.ExecuteAsync(Command("""
                INSERT dbo.InboundDocument(Uid,Version,IsDeleted,DocumentNo,DocumentType,CreationMode,LifecycleStatus,PostingStatus,
                    WarehouseSiteId,WarehouseCodeSnapshot,WarehouseNameSnapshot,ReceiptDate,Remark,CommandId,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy)
                VALUES(@Uid,1,0,@DocumentNo,@DocumentType,@CreationMode,@LifecycleStatus,@PostingStatus,
                    @WarehouseSiteId,@WarehouseCodeSnapshot,@WarehouseNameSnapshot,@ReceiptDate,@Remark,@CommandId,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy);
                """, header, token));
            header.Version = 1;
        }
        else
        {
            context.AuditStamp.StampForUpdate(header);
            var changed = await context.Session.Connection.ExecuteAsync(Command("""
                UPDATE dbo.InboundDocument SET WarehouseSiteId=@WarehouseSiteId,WarehouseCodeSnapshot=@WarehouseCodeSnapshot,
                    WarehouseNameSnapshot=@WarehouseNameSnapshot,ReceiptDate=@ReceiptDate,Remark=@Remark,
                    Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
                WHERE Uid=@Uid AND Version=@Version AND IsDeleted=0 AND CreationMode=0 AND PostingStatus=0 AND LifecycleStatus=0;
                """, header, token));
            if (changed != 1) throw new System.Data.DBConcurrencyException("入库草稿发生并发冲突。");
            header.Version++;
        }
        // 整单版本已经锁定；先软删省略行，保留稳定明细 Uid，不重建全部明细身份。
        await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.InboundDocumentLine SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE InboundDocumentId=@Uid AND IsDeleted=0 AND Uid NOT IN @Ids;
            """, new { header.Uid, header.UpdatedDate, header.UpdatedBy, Ids = lines.Select(x => x.Uid).ToArray() }, token));
        foreach (var line in lines)
        {
            var isNew = line.Version == 0;
            if (isNew) context.AuditStamp.StampForInsert(line); else context.AuditStamp.StampForUpdate(line);
            var count = await context.Session.Connection.ExecuteAsync(Command(isNew ? """
                INSERT dbo.InboundDocumentLine(Uid,Version,IsDeleted,InboundDocumentId,LineNumber,ProductId,SkuSnapshot,DescriptionSnapshot,
                    UnitSnapshot,Quantity,SourceLineId,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy)
                VALUES(@Uid,1,0,@InboundDocumentId,@LineNumber,@ProductId,@SkuSnapshot,@DescriptionSnapshot,
                    @UnitSnapshot,@Quantity,@SourceLineId,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy);
                """ : """
                UPDATE dbo.InboundDocumentLine SET LineNumber=@LineNumber,Quantity=@Quantity,Version=Version+1,
                    UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
                WHERE Uid=@Uid AND InboundDocumentId=@InboundDocumentId AND Version=@Version AND IsDeleted=0;
                """, line, token));
            if (count != 1) throw new System.Data.DBConcurrencyException("入库明细版本已变化，保存失败。");
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(InboundDocument header, CancellationToken token)
    {
        context.AuditStamp.StampForUpdate(header);
        var count = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.InboundDocument SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@Uid AND Version=@Version AND IsDeleted=0 AND CreationMode=0 AND PostingStatus=0 AND LifecycleStatus=0;
            """, header, token));
        if (count != 1) throw new System.Data.DBConcurrencyException("入库草稿删除发生并发冲突。");
        await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.InboundDocumentLine SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE InboundDocumentId=@Uid AND IsDeleted=0;
            """, header, token));
        header.Version++;
    }

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
    /// <inheritdoc/>
    public async Task MarkReversedAsync(InboundDocument header, CancellationToken token)
    {
        context.AuditStamp.StampForUpdate(header);
        var count = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.InboundDocument SET PostingStatus=8,ReversedDate=@ReversedDate,ReversedBy=@ReversedBy,
                ReverseReason=@ReverseReason,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy,Version=Version+1
            WHERE Uid=@Uid AND Version=@Version AND PostingStatus=1 AND LifecycleStatus=0 AND IsDeleted=0
                AND DocumentType=10 AND CreationMode=0 AND TriggerPackingListId IS NULL;
            """, header, token));
        if (count != 1) throw new DBConcurrencyException("入库状态或版本已变化，反过账未完成。");
        header.Version++;
    }
}
