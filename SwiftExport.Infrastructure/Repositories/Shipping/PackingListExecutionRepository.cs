using Dapper;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>同事务追加正式执行证据，使用专用状态 SQL 防止绕过草稿限制。</summary>
public sealed class PackingListExecutionRepository(RepositoryBaseContext context) : IPackingListExecutionRepository
{
    /// <inheritdoc/>
    public Task<bool> IsSchemaReadyAsync(CancellationToken token) => context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
        SELECT CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.PackingListPostingExecution',N'U') IS NOT NULL
          AND COL_LENGTH(N'dbo.PackingListPostingExecution',N'ResultJson') IS NOT NULL
          AND COL_LENGTH(N'dbo.PackingListPostingExecutionLine',N'OriginalExecutionLineId') IS NOT NULL
          AND EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PackingListPostingExecution')
              AND name=N'UX_PackingListPostingExecution_Command' AND is_unique=1 AND is_disabled=0)
          THEN 1 ELSE 0 END);
        """, null, token));

    /// <summary>命令结果只追加；正式编排已持有命令应用锁，读取不应保留不存在键的 Serializable 范围锁。
    /// 否则不同命令可能各占执行索引空隙，再等待日志锁，阻止另一事务插入执行记录而死锁。</summary>
    public async Task<PackingListPostingExecution?> FindCommandAsync(Guid commandId, CancellationToken token) =>
        (await context.Session.Connection.QueryAsync<PackingListPostingExecution>(Command(
            $"SELECT {Columns<PackingListPostingExecution>()} FROM dbo.PackingListPostingExecution WITH (READCOMMITTED) WHERE CommandId=@commandId;", new { commandId }, token))).SingleOrDefault();

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<PackingListPostingExecution> Heads, IReadOnlyList<PackingListPostingExecutionLine> Lines)> ReadAsync(Guid headId, CancellationToken token)
    {
        var headers = (await context.Session.Connection.QueryAsync<PackingListPostingExecution>(Command(
            $"SELECT {Columns<PackingListPostingExecution>()} FROM dbo.PackingListPostingExecution WHERE PackingListHeadId=@headId ORDER BY CreatedDate,Uid;", new { headId }, token))).ToArray();
        var details = (await context.Session.Connection.QueryAsync<PackingListPostingExecutionLine>(Command(
            $"SELECT {Columns<PackingListPostingExecutionLine>()} FROM dbo.PackingListPostingExecutionLine WHERE ExecutionId IN @Ids ORDER BY ExecutionId,LineNumber;",
            new { Ids = headers.Select(x => x.Uid).ToArray() }, token))).ToArray();
        return (headers, details);
    }

    /// <inheritdoc/>
    public async Task AppendAsync(PackingListPostingExecution execution, IReadOnlyList<PackingListPostingExecutionLine> details, CancellationToken token)
    {
        RequireTransaction();
        if (details.Any(x => x.ExecutionId != execution.Uid)) throw new InvalidOperationException("执行证据头行归属不一致。");
        token.ThrowIfCancellationRequested();
        await InsertEvidenceAsync(execution, token);
        foreach (var line in details)
        {
            token.ThrowIfCancellationRequested();
            await InsertEvidenceAsync(line, token);
        }
        token.ThrowIfCancellationRequested();
    }

    /// <inheritdoc/>
    public async Task MarkAsync(PackingListHead head, int expectedVersion, bool reverse, CancellationToken token)
    {
        RequireTransaction();
        context.AuditStamp.StampForUpdate(head);
        var sql = reverse ? """
            UPDATE dbo.PackingListHead SET PostingStatus=8,ReverseCommandId=@ReverseCommandId,
              ReversedDate=@ReversedDate,ReversedBy=@ReversedBy,ReverseReason=@ReverseReason,
              UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy,Version=Version+1
            WHERE Uid=@Uid AND Version=@ExpectedVersion AND IsDeleted=0 AND PostingStatus=1;
            """ : """
            UPDATE dbo.PackingListHead SET PostingStatus=1,PostCommandId=@PostCommandId,PostingDate=@PostingDate,
              PostedDate=@PostedDate,PostedBy=@PostedBy,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy,Version=Version+1
            WHERE Uid=@Uid AND Version=@ExpectedVersion AND IsDeleted=0 AND LifecycleStatus=0 AND PostingStatus=0;
            """;
        var parameters = new DynamicParameters(head);
        parameters.Add("ExpectedVersion", expectedVersion);
        if (await context.Session.Connection.ExecuteAsync(Command(sql, parameters, token)) != 1)
            throw new InvalidOperationException("箱单状态或版本发生变化。");
        head.Version = expectedVersion + 1;
    }

    /// <inheritdoc/>
    public async Task<PackingListReverseFacts> ReadReverseFactsAsync(Guid headId, CancellationToken token)
    {
        var history = await ReadAsync(headId, token);
        var result = new PackingListReverseFacts { Executions = history.Heads, Evidence = history.Lines };
        result.Head = (await ReadRows<PackingListHead>("PackingListHead", "Uid=@headId", new { headId }, token)).SingleOrDefault();
        result.PackingLines = await ReadRows<PackingListLine>("PackingListLine", "PackingListHeadId=@headId", new { headId }, token);
        // 同时读取触发关系及证据引用目标，跨箱单误关联和未登记的自动单不能被过滤隐藏。
        var documents = history.Lines.Select(x => x.DocumentId).OfType<Guid>().Distinct().ToArray();
        result.InboundHeads = await ReadRows<InboundDocument>("InboundDocument", "TriggerPackingListId=@headId OR Uid IN @documents", new { headId, documents }, token);
        result.OutboundHeads = await ReadRows<OutboundDocument>("OutboundDocument", "TriggerPackingListId=@headId OR Uid IN @documents", new { headId, documents }, token);
        var inboundIds = result.InboundHeads.Select(x => x.Uid).ToArray();
        var outboundIds = result.OutboundHeads.Select(x => x.Uid).ToArray();
        result.InboundLines = await ReadRows<InboundDocumentLine>("InboundDocumentLine", "InboundDocumentId IN @Ids", new { Ids = inboundIds }, token);
        result.OutboundLines = await ReadRows<OutboundDocumentLine>("OutboundDocumentLine", "OutboundDocumentId IN @Ids", new { Ids = outboundIds }, token);
        var ledgerIds = history.Lines.Select(x => x.LedgerId).OfType<Guid>().Distinct().ToArray();
        result.Ledgers = await ReadRows<InventoryLedgerEntry>("InventoryLedgerEntry", """
            SourceDocumentId IN @Ids OR Uid IN @LedgerIds OR ReversalOfLedgerId IN @LedgerIds OR ReplacementOfLedgerId IN @LedgerIds
            """, new { Ids = inboundIds.Concat(outboundIds).Distinct().ToArray(), LedgerIds = ledgerIds }, token);
        var warehouseIds = history.Lines.Select(x => x.WarehouseSiteId).OfType<Guid>().Distinct().ToArray();
        var productIds = history.Lines.Select(x => x.ProductId).OfType<Guid>().Distinct().ToArray();
        result.Warehouses = await ReadRows<WarehouseSite>("WarehouseSite", "Uid IN @Ids", new { Ids = warehouseIds }, token);
        result.Balances = await ReadRows<InventoryBalance>("InventoryBalance", "WarehouseSiteId IN @warehouseIds AND ProductId IN @productIds",
            new { warehouseIds, productIds }, token);
        return result;
    }

    /// <summary>反向批量事实查询只使用本类固定表名及谓词，参数携带业务身份。</summary>
    private async Task<IReadOnlyList<T>> ReadRows<T>(string table, string predicate, object parameters, CancellationToken token) =>
        (await context.Session.Connection.QueryAsync<T>(Command($"SELECT {Columns<T>()} FROM dbo.[{table}] WHERE {predicate} ORDER BY Uid;", parameters, token))).ToArray();

    /// <summary>执行身份已参与结果 JSON、头行及反向映射，专用插入不得重建 Uid。</summary>
    private async Task InsertEvidenceAsync<T>(T entity, CancellationToken token) where T : SwiftExport.Core.Common.BaseEntity
    {
        RequireTransaction();
        if (entity.Uid == Guid.Empty) throw new InvalidOperationException("执行证据缺少稳定身份。");
        context.AuditStamp.StampForInsert(entity);
        entity.IsDeleted = false; entity.Version = Math.Max(1, entity.Version);
        var names = typeof(T).GetProperties().Where(p => p.CanRead && p.CanWrite).Select(p => p.Name).ToArray();
        var sql = $"INSERT dbo.[{typeof(T).Name}] ({string.Join(',', names.Select(n => $"[{n}]"))}) VALUES ({string.Join(',', names.Select(n => "@" + n))});";
        if (await context.Session.Connection.ExecuteAsync(Command(sql, entity, token)) != 1)
            throw new InvalidOperationException("执行证据写入失败。");
    }

    /// <summary>只有调用方已启动事务才能写入证据和状态。</summary>
    private void RequireTransaction()
    {
        if (context.Session.Transaction is null) throw new InvalidOperationException("执行证据必须加入外层事务。");
    }
    /// <summary>固定实体字段白名单，不拼接客户端列名。</summary>
    private static string Columns<T>() => string.Join(',', typeof(T).GetProperties().Where(x => x.CanRead && x.CanWrite).Select(x => $"[{x.Name}]"));
    /// <summary>使用调用方的同连接、同事务和取消令牌。</summary>
    private CommandDefinition Command(string sql, object? parameters, CancellationToken token) => new(sql, parameters, context.Session.Transaction, cancellationToken: token);
}
