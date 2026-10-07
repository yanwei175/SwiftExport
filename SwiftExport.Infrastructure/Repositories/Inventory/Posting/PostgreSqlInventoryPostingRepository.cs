using System.Buffers.Binary;
using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Inventory.Posting;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Posting;

/// <summary>在原父事务中读取库存事实、维护余额并追加流水；局部事务锁替代库存命令应用锁及不存在明细身份的间隙保护。</summary>
public sealed class PostgreSqlInventoryPostingRepository(RepositoryBaseContext context) : IInventoryPostingRepository
{
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string BalanceInsertSql = """
        INSERT INTO "public"."inventory_balance" ("uid",
        "version",
        "is_deleted",
        "created_date",
        "created_by",
        "updated_date",
        "updated_by",
        "warehouse_site_id",
        "product_id",
        "sku_snapshot",
        "product_name_snapshot",
        "unit_snapshot",
        "on_hand_qty",
        "last_ledger_sequence") VALUES (@Uid,
        @Version,
        @IsDeleted,
        @CreatedDate,
        @CreatedBy,
        @UpdatedDate,
        @UpdatedBy,
        @WarehouseSiteId,
        @ProductId,
        @SkuSnapshot,
        @ProductNameSnapshot,
        @UnitSnapshot,
        @OnHandQty,
        @LastLedgerSequence);
    """;
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string LedgerInsertSql = """
        INSERT INTO "public"."inventory_ledger_entry" ("uid",
        "version",
        "is_deleted",
        "created_date",
        "created_by",
        "updated_date",
        "updated_by",
        "sequence",
        "warehouse_site_id",
        "product_id",
        "transaction_type",
        "quantity_delta",
        "balance_after",
        "source_document_type",
        "source_document_id",
        "source_line_id",
        "source_document_no_snapshot",
        "reversal_of_ledger_id",
        "replacement_of_ledger_id",
        "correction_group_id",
        "command_id",
        "command_item_id",
        "request_fingerprint",
        "reason",
        "occurred_date",
        "warehouse_code_snapshot",
        "warehouse_name_snapshot",
        "sku_snapshot",
        "product_name_snapshot",
        "unit_snapshot") VALUES (@Uid,
        @Version,
        @IsDeleted,
        @CreatedDate,
        @CreatedBy,
        @UpdatedDate,
        @UpdatedBy,
        @Sequence,
        @WarehouseSiteId,
        @ProductId,
        @TransactionType,
        @QuantityDelta,
        @BalanceAfter,
        @SourceDocumentType,
        @SourceDocumentId,
        @SourceLineId,
        @SourceDocumentNoSnapshot,
        @ReversalOfLedgerId,
        @ReplacementOfLedgerId,
        @CorrectionGroupId,
        @CommandId,
        @CommandItemId,
        @RequestFingerprint,
        @Reason,
        @OccurredDate,
        @WarehouseCodeSnapshot,
        @WarehouseNameSnapshot,
        @SkuSnapshot,
        @ProductNameSnapshot,
        @UnitSnapshot);
    """;

    private static readonly EntityMetadata BalanceMap = EntityMetadataCache.GetMetadata(typeof(InventoryBalance));
    private static readonly EntityMetadata LedgerMap = EntityMetadataCache.GetMetadata(typeof(InventoryLedgerEntry));

    /// <summary>借原会话锁余额行；生产写入调用方先锁既有仓库，空余额和高隔离联合协议须按实际调用链验收。</summary>
    public Task<InventoryBalance?> LockBalanceAsync(Guid warehouseSiteId, Guid productId) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<InventoryBalance>(Command("""
            SELECT "uid" AS "Uid",
            "version" AS "Version",
            "is_deleted" AS "IsDeleted",
            "created_date" AS "CreatedDate",
            "created_by" AS "CreatedBy",
            "updated_date" AS "UpdatedDate",
            "updated_by" AS "UpdatedBy",
            "warehouse_site_id" AS "WarehouseSiteId",
            "product_id" AS "ProductId",
            "sku_snapshot" AS "SkuSnapshot",
            "product_name_snapshot" AS "ProductNameSnapshot",
            "unit_snapshot" AS "UnitSnapshot",
            "on_hand_qty" AS "OnHandQty",
            "last_ledger_sequence" AS "LastLedgerSequence" FROM public.inventory_balance WHERE warehouse_site_id=@WarehouseSiteId AND product_id=@ProductId FOR UPDATE
        """, new { WarehouseSiteId = warehouseSiteId, ProductId = productId }));

    /// <summary>按明细幂等键普通读取，不增加软删条件或事务要求。</summary>
    public Task<InventoryLedgerEntry?> FindLedgerByCommandItemAsync(Guid commandItemId) => QueryLedgerAsync("command_item_id=@Id", commandItemId, false);

    /// <summary>父事务中先保护全局明细身份，再读取并锁现有事实；无父事务时保持源语句锁的短寿命。</summary>
    public async Task<InventoryLedgerEntry?> LockLedgerByCommandItemAsync(Guid commandItemId)
    {
        if (context.Session.Transaction is not null && await AcquireResourceAsync($"InventoryItem:{commandItemId:N}", default) < 0)
            throw new TimeoutException("库存明细身份正在处理中。");
        return await QueryLedgerAsync("command_item_id=@Id", commandItemId, true);
    }

    /// <summary>锁原流水实际身份，不改原不存在返回空的语义。</summary>
    public Task<InventoryLedgerEntry?> LockLedgerByIdAsync(Guid ledgerId) => QueryLedgerAsync("uid=@Id", ledgerId, true);

    /// <summary>先锁原事实作为已有原流水的反向集合锚点，再锁对应反向事实；高隔离首次快照另验。</summary>
    public async Task<InventoryLedgerEntry?> LockReversalByOriginalAsync(Guid originalLedgerId)
    {
        await QueryLedgerAsync("uid=@Id", originalLedgerId, true);
        return await QueryLedgerAsync("reversal_of_ledger_id=@Id", originalLedgerId, true);
    }

    /// <summary>复用原事实行作为替代集合锚点，保留所有状态事实和不存在返回空。</summary>
    public async Task<InventoryLedgerEntry?> LockReplacementByOriginalAsync(Guid originalLedgerId)
    {
        await QueryLedgerAsync("uid=@Id", originalLedgerId, true);
        return await QueryLedgerAsync("replacement_of_ledger_id=@Id", originalLedgerId, true);
    }

    /// <summary>按原序号与SqlGuid读取纠正组；实际纠正调用方先取得同一组命令事务锁，不新增全局库存锁。</summary>
    public async Task<IReadOnlyList<InventoryLedgerEntry>> LockCorrectionGroupAsync(Guid correctionGroupId) =>
        (await context.Session.Connection.QueryAsync<InventoryLedgerEntry>(Command($"""
            SELECT "uid" AS "Uid",
            "version" AS "Version",
            "is_deleted" AS "IsDeleted",
            "created_date" AS "CreatedDate",
            "created_by" AS "CreatedBy",
            "updated_date" AS "UpdatedDate",
            "updated_by" AS "UpdatedBy",
            "sequence" AS "Sequence",
            "warehouse_site_id" AS "WarehouseSiteId",
            "product_id" AS "ProductId",
            "transaction_type" AS "TransactionType",
            "quantity_delta" AS "QuantityDelta",
            "balance_after" AS "BalanceAfter",
            "source_document_type" AS "SourceDocumentType",
            "source_document_id" AS "SourceDocumentId",
            "source_line_id" AS "SourceLineId",
            "source_document_no_snapshot" AS "SourceDocumentNoSnapshot",
            "reversal_of_ledger_id" AS "ReversalOfLedgerId",
            "replacement_of_ledger_id" AS "ReplacementOfLedgerId",
            "correction_group_id" AS "CorrectionGroupId",
            "command_id" AS "CommandId",
            "command_item_id" AS "CommandItemId",
            "request_fingerprint" AS "RequestFingerprint",
            "reason" AS "Reason",
            "occurred_date" AS "OccurredDate",
            "warehouse_code_snapshot" AS "WarehouseCodeSnapshot",
            "warehouse_name_snapshot" AS "WarehouseNameSnapshot",
            "sku_snapshot" AS "SkuSnapshot",
            "product_name_snapshot" AS "ProductNameSnapshot",
            "unit_snapshot" AS "UnitSnapshot" FROM public.inventory_ledger_entry WHERE correction_group_id=@Id ORDER BY sequence,
            {UuidOrder("uid")} FOR UPDATE
        """, new { Id = correctionGroupId }))).AsList();

    /// <summary>保持源最小产品投影和未软删资格，不新增启用、供应或类型过滤。</summary>
    public Task<Product?> GetProductForPostingAsync(Guid productId) => context.Session.Connection.QuerySingleOrDefaultAsync<Product>(Command("""
        SELECT uid AS "Uid",sku AS "Sku",unit AS "Unit",description_zh AS "DescriptionZh",description_en AS "DescriptionEn"
        FROM public.product WHERE uid=@Id AND is_deleted=FALSE;
        """, new { Id = productId }));

    /// <summary>保留库存命令独立身份与15秒竞争预算，事务锁随原父事务释放，取消与数据库异常原样传播。</summary>
    public Task<int> AcquireCommandLockAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        if (context.Session.Transaction is null) throw new InvalidOperationException("库存命令应用锁必须属于业务事务。");
        return AcquireResourceAsync($"InventoryCommand:{commandId:N}", cancellationToken);
    }

    /// <summary>按原全部余额字段打戳插入，保留输入身份、版本及返回影响行数，不自行提交。</summary>
    public async Task<int> InsertBalanceAsync(InventoryBalance balance)
    {
        context.AuditStamp.StampForInsert(balance);
        return await context.Session.Connection.ExecuteAsync(Command(BalanceInsertSql, PostgreSqlParameters.Entity(balance, BalanceMap)));
    }

    /// <summary>保持源乐观版本谓词、数据库版本递增和本地审计时间；不回写实体Version或改变删行资格。</summary>
    public Task<int> UpdateBalanceAsync(InventoryBalance balance, int expectedVersion)
    {
        var parameters = new PostgreSqlParameters();
        foreach (var name in new[] { "Uid", "SkuSnapshot", "ProductNameSnapshot", "UnitSnapshot", "OnHandQty", "LastLedgerSequence" })
            parameters.Add(name, BalanceMap.GetProperty(name).Property.GetValue(balance));
        parameters.Add("ExpectedVersion", expectedVersion); parameters.AddTimestamp("Now", DateTime.Now); parameters.Add("UserName", context.CurrentUser.UserName);
        return context.Session.Connection.ExecuteAsync(Command("""
            UPDATE public.inventory_balance SET sku_snapshot=@SkuSnapshot,product_name_snapshot=@ProductNameSnapshot,
                unit_snapshot=@UnitSnapshot,on_hand_qty=@OnHandQty,last_ledger_sequence=@LastLedgerSequence,
                version=version+1,updated_date=@Now,updated_by=@UserName WHERE uid=@Uid AND version=@ExpectedVersion;
            """, parameters));
    }

    /// <summary>取得原全局单调流水序号，PG序列与SQL Server相同不随父事务回滚。</summary>
    public Task<long> NextLedgerSequenceAsync() => context.Session.Connection.ExecuteScalarAsync<long>(Command("SELECT nextval('public.inventory_ledger_sequence');", null));

    /// <summary>打戳后按原完整流水字段追加事实，不更新旧流水、不回写输入版本或自行处理冲突。</summary>
    public async Task<int> InsertLedgerAsync(InventoryLedgerEntry entry)
    {
        context.AuditStamp.StampForInsert(entry);
        return await context.Session.Connection.ExecuteAsync(Command(LedgerInsertSql, PostgreSqlParameters.Entity(entry, LedgerMap)));
    }

    /// <summary>局部固定资源摘要只用于必要事务互斥，不建立提供者框架、会话租约或业务重试；高隔离快照不能被该等待刷新。</summary>
    private async Task<int> AcquireResourceAsync(string resource, CancellationToken token)
    {
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("SwiftExport:" + resource)));
        var elapsed = Stopwatch.StartNew(); var budget = TimeSpan.FromSeconds(15);
        while (elapsed.Elapsed < budget)
        {
            token.ThrowIfCancellationRequested();
            if (await context.Session.Connection.ExecuteScalarAsync<bool>(Command("SELECT pg_try_advisory_xact_lock(@Key);", new { Key = key }, token))) return 0;
            var remaining = budget - elapsed.Elapsed; if (remaining <= TimeSpan.Zero) break;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50), token);
        }
        return -1;
    }

    /// <summary>固定谓词只由类内代码提供，显式投影保持原实体别名，父事务原样借用。</summary>
    private Task<InventoryLedgerEntry?> QueryLedgerAsync(string where, Guid id, bool forUpdate) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<InventoryLedgerEntry>(Command($"""
            SELECT "uid" AS "Uid",
            "version" AS "Version",
            "is_deleted" AS "IsDeleted",
            "created_date" AS "CreatedDate",
            "created_by" AS "CreatedBy",
            "updated_date" AS "UpdatedDate",
            "updated_by" AS "UpdatedBy",
            "sequence" AS "Sequence",
            "warehouse_site_id" AS "WarehouseSiteId",
            "product_id" AS "ProductId",
            "transaction_type" AS "TransactionType",
            "quantity_delta" AS "QuantityDelta",
            "balance_after" AS "BalanceAfter",
            "source_document_type" AS "SourceDocumentType",
            "source_document_id" AS "SourceDocumentId",
            "source_line_id" AS "SourceLineId",
            "source_document_no_snapshot" AS "SourceDocumentNoSnapshot",
            "reversal_of_ledger_id" AS "ReversalOfLedgerId",
            "replacement_of_ledger_id" AS "ReplacementOfLedgerId",
            "correction_group_id" AS "CorrectionGroupId",
            "command_id" AS "CommandId",
            "command_item_id" AS "CommandItemId",
            "request_fingerprint" AS "RequestFingerprint",
            "reason" AS "Reason",
            "occurred_date" AS "OccurredDate",
            "warehouse_code_snapshot" AS "WarehouseCodeSnapshot",
            "warehouse_name_snapshot" AS "WarehouseNameSnapshot",
            "sku_snapshot" AS "SkuSnapshot",
            "product_name_snapshot" AS "ProductNameSnapshot",
            "unit_snapshot" AS "UnitSnapshot" FROM public.inventory_ledger_entry WHERE {where}{(forUpdate ? " FOR UPDATE" : "")}
        """, new { Id = id }));

    /// <summary>透传当前事务，不更改连接设置或截获业务异常。</summary>
    private CommandDefinition Command(string sql, object? parameters, CancellationToken token = default) => new(sql, parameters, context.Session.Transaction, cancellationToken: token);
    /// <summary>保留源uniqueidentifier排序优先级。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(i => $"get_byte(uuid_send({column}),{i})")) + "]";
}
