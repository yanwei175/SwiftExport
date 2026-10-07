using Dapper;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;

/// <summary>在调用方 PG 会话内读取出库执行与反向所需事实，不写余额或流水。</summary>
public sealed class PostgreSqlOutboundExecutionReadRepository(RepositoryBaseContext context) : IOutboundExecutionReadRepository
{
    private const string ReverseLedgerColumns = """
        e."uid" AS "Uid",
        e."command_id" AS "CommandId",
        e."command_item_id" AS "CommandItemId",
        e."warehouse_site_id" AS "WarehouseSiteId",
        e."product_id" AS "ProductId",
        e."quantity_delta" AS "QuantityDelta",
        e."unit_snapshot" AS "UnitSnapshot",
        e."sku_snapshot" AS "SkuSnapshot",
        e."transaction_type" AS "TransactionType",
        e."source_document_type" AS "SourceDocumentType",
        e."source_document_id" AS "SourceDocumentId",
        e."source_line_id" AS "SourceLineId",
        e."reversal_of_ledger_id" AS "ReversalOfLedgerId",
        e."replacement_of_ledger_id" AS "ReplacementOfLedgerId",
        e."correction_group_id" AS "CorrectionGroupId",
        e."is_deleted" AS "IsDeleted"
        """;

    /// <summary>只读汇总同仓同产品未删余额，无记录返回零，不预占库存。</summary>
    public Task<decimal> ReadBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<decimal>(Command($"""
            SELECT COALESCE(SUM(b."on_hand_qty"),0) FROM "public"."inventory_balance" b
            WHERE b."warehouse_site_id"=@WarehouseId AND b."product_id"=@ProductId
                AND b."is_deleted"=FALSE;
            """, new { WarehouseId = warehouseId, ProductId = productId }, token));

    /// <summary>读取有效且启用仓库的名称，缺失或停用返回空。</summary>
    public Task<string?> ReadActiveWarehouseNameAsync(Guid warehouseId, CancellationToken token) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<string>(Command($"""
            SELECT w."name" FROM "public"."warehouse_site" w
            WHERE w."uid"=@WarehouseId AND w."is_active"=TRUE
                AND w."is_deleted"=FALSE;
            """, new { WarehouseId = warehouseId }, token));

    /// <summary>读取原单及一次关联的反向、替代流水，保留删除行与历史异常事实供服务判断。</summary>
    public async Task<IReadOnlyList<InventoryLedgerEntry>> ReadReverseLedgersAsync(Guid uid, CancellationToken token)
    {
        var rows = await context.Session.Connection.QueryAsync<InventoryLedgerEntry>(Command($"""
            WITH direct_facts AS (
                SELECT e."uid" AS uid FROM "public"."inventory_ledger_entry" e
                WHERE e."source_document_id"=@Uid OR EXISTS(
                    SELECT 1 FROM "public"."outbound_document_line" l WHERE l."outbound_document_id"=@Uid
                        AND (l."uid"=e."source_line_id"
                            OR l."command_item_id"=e."command_item_id"))
            ), related_facts AS (
                SELECT uid FROM direct_facts UNION
                SELECT e."uid" FROM "public"."inventory_ledger_entry" e
                WHERE e."reversal_of_ledger_id" IN(SELECT uid FROM direct_facts)
                    OR e."replacement_of_ledger_id" IN(SELECT uid FROM direct_facts)
            )
            SELECT {ReverseLedgerColumns} FROM "public"."inventory_ledger_entry" e JOIN related_facts r ON r.uid=e."uid"
            ORDER BY {UuidOrder("e.\"uid\"")};
            """, new { Uid = uid }, token));
        return rows.AsList();
    }

    /// <summary>读取包含软删状态的原余额，缺失不补建，异常由反向服务判断。</summary>
    public Task<InventoryBalance?> ReadReverseBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token)
    {
        return context.Session.Connection.QuerySingleOrDefaultAsync<InventoryBalance>(Command($"""
            SELECT
            b."uid" AS "Uid",
            b."warehouse_site_id" AS "WarehouseSiteId",
            b."product_id" AS "ProductId",
            b."on_hand_qty" AS "OnHandQty",
            b."unit_snapshot" AS "UnitSnapshot",
            b."is_deleted" AS "IsDeleted"
            FROM "public"."inventory_balance" b WHERE b."warehouse_site_id"=@WarehouseId
                AND b."product_id"=@ProductId;
            """, new { WarehouseId = warehouseId, ProductId = productId }, token));
    }

    /// <summary>允许停用仓库用于历史反向，仅要求身份存在且未删除。</summary>
    public Task<bool> ReverseWarehouseExistsAsync(Guid warehouseId, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command($"""
            SELECT COUNT(*)>0 FROM "public"."warehouse_site" w
            WHERE w."uid"=@Uid AND w."is_deleted"=FALSE;
            """, new { Uid = warehouseId }, token));

    /// <summary>只透传当前事务和取消，不开始或结束业务事务。</summary>
    private CommandDefinition Command(string sql, object args, CancellationToken token) => new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <summary>保留原 uniqueidentifier 排序，不以 PG UUID 原生顺序替代。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(index => $"get_byte(uuid_send({column}),{index})")) + "]";
}
