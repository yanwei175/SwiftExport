using Dapper;
using System.Data;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Inventory.AutomaticDocuments;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory.AutomaticDocuments;

/// <summary>持久化自动单据及状态；专用插入保留命令派生的稳定身份和头行关联。</summary>
public sealed class AutomaticDocumentRepository(RepositoryBaseContext context) : IAutomaticDocumentRepository
{
    private CommandDefinition Command(string sql, object? args, CancellationToken token) =>
        new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <inheritdoc/>
    public async Task<bool> IsSchemaReadyAsync(CancellationToken token)
    {
        // 只读取元数据，缺列或仍有单据内产品唯一约束时拒绝，不自动迁移。
        foreach (var type in new[] { typeof(InboundDocument), typeof(InboundDocumentLine), typeof(OutboundDocument), typeof(OutboundDocumentLine) })
        {
            var names = (await context.Session.Connection.QueryAsync<string>(Command(
                "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(@TableName)", new { TableName = "dbo." + type.Name }, token))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (type.GetProperties().Any(p => p.CanWrite && !names.Contains(p.Name))) return false;
        }
        return !await context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CONVERT(bit,CASE WHEN EXISTS(
                SELECT 1 FROM sys.check_constraints
                WHERE parent_object_id IN(OBJECT_ID(N'dbo.InboundDocument'),OBJECT_ID(N'dbo.OutboundDocument'))
                  AND is_disabled=0 AND (definition LIKE N'%DocumentType%' OR definition LIKE N'%CreationMode%')
            ) OR EXISTS(
                SELECT i.index_id FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0
                JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                WHERE i.object_id=OBJECT_ID(N'dbo.OutboundDocumentLine') AND i.is_unique=1 AND i.is_disabled=0
                GROUP BY i.index_id
                HAVING COUNT(*)=2 AND SUM(CASE WHEN c.name IN(N'OutboundDocumentId',N'ProductId') THEN 1 ELSE 0 END)=2
            ) THEN 1 ELSE 0 END);
            """, null, token));
    }

    /// <inheritdoc/>
    public async Task LockProductsAsync(IReadOnlyList<Guid> productIds, CancellationToken token)
    {
        foreach (var id in productIds.Distinct().Order())
            await context.Session.Connection.QueryAsync<Guid>(Command("SELECT Uid FROM dbo.Product WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Id", new { Id = id }, token));
    }

    /// <inheritdoc/>
    public Task<Guid?> GetPurchaseSupplierAsync(Guid purchaseLineId, CancellationToken token) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<Guid?>(Command("""
            SELECT h.SupplierId FROM dbo.PurchaseOrderLine l
            JOIN dbo.PurchaseOrderHead h ON h.Uid=l.PurchaseOrderId
            WHERE l.Uid=@Id AND l.IsDeleted=0 AND h.IsDeleted=0;
            """, new { Id = purchaseLineId }, token));

    /// <inheritdoc/>
    public async Task InsertInboundAsync(InboundDocument header, IReadOnlyList<InboundDocumentLine> lines, CancellationToken token)
    {
        await AddAsync(header, token);
        foreach (var line in lines) await AddAsync(line, token);
    }

    /// <inheritdoc/>
    public async Task InsertOutboundAsync(OutboundDocument header, IReadOnlyList<OutboundDocumentLine> lines, CancellationToken token)
    {
        await AddAsync(header, token);
        foreach (var line in lines) await AddAsync(line, token);
    }

    /// <summary>不能使用会重建 Uid 的通用 Add；保留调用方身份，在同事务内打戳并插入。</summary>
    private async Task AddAsync<T>(T entity, CancellationToken token) where T : BaseEntity
    {
        token.ThrowIfCancellationRequested();
        if (context.Session.Transaction == null || entity.Uid == Guid.Empty) throw new InvalidOperationException("自动单据插入必须具有事务及稳定身份。");
        context.AuditStamp.StampForInsert(entity);
        entity.IsDeleted = false; entity.Version = Math.Max(1, entity.Version);
        var names = typeof(T).GetProperties().Where(p => p.CanRead && p.CanWrite).Select(p => p.Name).ToArray();
        var sql = $"INSERT dbo.[{typeof(T).Name}] ({string.Join(',', names.Select(n => $"[{n}]"))}) VALUES ({string.Join(',', names.Select(n => "@" + n))});";
        if (await context.Session.Connection.ExecuteAsync(Command(sql, entity, token)) != 1)
            throw new DBConcurrencyException("自动单据写入失败。");
    }

    /// <inheritdoc/>
    public Task SetInboundStateAsync(InboundDocument header, bool reverse, CancellationToken token) =>
        SetStateAsync(header, "InboundDocument", "DocumentType=10 AND CreationMode=1", reverse, token);

    /// <inheritdoc/>
    public Task SetOutboundStateAsync(OutboundDocument header, bool reverse, CancellationToken token) =>
        SetStateAsync(header, "OutboundDocument", "DocumentType IN(20,21)", reverse, token);

    /// <summary>表名和业务谓词仅来自固定代码；原状态、箱单归属及版本均必须匹配。</summary>
    private async Task SetStateAsync(BaseEntity entity, string table, string kind, bool reverse, CancellationToken token)
    {
        context.AuditStamp.StampForUpdate(entity);
        var state = reverse
            ? "PostingStatus=8,ReversedDate=@ReversedDate,ReversedBy=@ReversedBy,ReverseReason=@ReverseReason"
            : "PostingStatus=1,PostedDate=@PostedDate,PostedBy=@PostedBy";
        var changed = await context.Session.Connection.ExecuteAsync(Command($"""
            UPDATE dbo.{table} SET {state},Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@Uid AND Version=@Version AND PostingStatus={(reverse ? 1 : 0)} AND LifecycleStatus=0
              AND IsDeleted=0 AND {kind} AND TriggerPackingListId=@TriggerPackingListId AND TriggerPackingListId IS NOT NULL;
            """, entity, token));
        if (changed != 1) throw new DBConcurrencyException("自动单据状态、版本或箱单归属已变化。");
        entity.Version++;
    }
}
