using Dapper;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

/// <summary>在调用方 PG 会话内读取入库执行与反向所需事实，不预占余额或修改流水。</summary>
public sealed class PostgreSqlInboundExecutionReadRepository(RepositoryBaseContext context) : IInboundExecutionReadRepository
{
    private static readonly string[] ReverseLedgerFields = ["Uid", "CommandId", "CommandItemId", "WarehouseSiteId", "ProductId", "QuantityDelta", "UnitSnapshot", "SkuSnapshot", "TransactionType", "SourceDocumentType", "SourceDocumentId", "SourceLineId", "ReversalOfLedgerId", "ReplacementOfLedgerId", "CorrectionGroupId", "IsDeleted"];

    /// <summary>保留单据身份或任意历史入库行身份命中的流水，包括软删行关联的异常事实。</summary>
    public Task<bool> HasLedgerAsync(Guid uid, CancellationToken token) => context.Session.Connection.ExecuteScalarAsync<bool>(Command($"""
        SELECT EXISTS(SELECT 1 FROM {Table<InventoryLedgerEntry>()} e WHERE {C<InventoryLedgerEntry>("e", "SourceDocumentId")}=@Uid)
            OR EXISTS(SELECT 1 FROM {Table<InventoryLedgerEntry>()} e
                INNER JOIN {Table<InboundDocumentLine>()} l ON {C<InboundDocumentLine>("l", "Uid")}={C<InventoryLedgerEntry>("e", "SourceLineId")}
                WHERE {C<InboundDocumentLine>("l", "InboundDocumentId")}=@Uid);
        """, new { Uid = uid }, token));

    /// <summary>只读汇总同仓同产品未删余额，无记录返回零，不取得库存锁。</summary>
    public Task<decimal> ReadBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<decimal>(Command($"""
            SELECT COALESCE(SUM({C<InventoryBalance>("b", "OnHandQty")}),0) FROM {Table<InventoryBalance>()} b
            WHERE {C<InventoryBalance>("b", "WarehouseSiteId")}=@WarehouseId AND {C<InventoryBalance>("b", "ProductId")}=@ProductId
                AND {C<InventoryBalance>("b", "IsDeleted")}=FALSE;
            """, new { WarehouseId = warehouseId, ProductId = productId }, token));

    /// <summary>读取有效且启用仓库的当前名称，缺失或停用返回空。</summary>
    public Task<string?> ReadActiveWarehouseNameAsync(Guid warehouseId, CancellationToken token) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<string>(Command($"""
            SELECT {C<WarehouseSite>("w", "Name")} FROM {Table<WarehouseSite>()} w
            WHERE {C<WarehouseSite>("w", "Uid")}=@WarehouseId AND {C<WarehouseSite>("w", "IsActive")}=TRUE
                AND {C<WarehouseSite>("w", "IsDeleted")}=FALSE;
            """, new { WarehouseId = warehouseId }, token));

    /// <summary>读取原单及一次关联的反向、替代流水；不排除软删事实，不递归扩展原查询范围。</summary>
    public async Task<IReadOnlyList<InventoryLedgerEntry>> ReadReverseLedgersAsync(Guid uid, CancellationToken token)
    {
        var fields = string.Join(',', ReverseLedgerFields.Select(field => C<InventoryLedgerEntry>("e", field) + " AS " + PostgreSqlNames.Quote(field)));
        var rows = await context.Session.Connection.QueryAsync<InventoryLedgerEntry>(Command($"""
            WITH direct_facts AS (
                SELECT {C<InventoryLedgerEntry>("e", "Uid")} AS uid FROM {Table<InventoryLedgerEntry>()} e
                WHERE {C<InventoryLedgerEntry>("e", "SourceDocumentId")}=@Uid OR EXISTS(
                    SELECT 1 FROM {Table<InboundDocumentLine>()} l WHERE {C<InboundDocumentLine>("l", "InboundDocumentId")}=@Uid
                        AND ({C<InboundDocumentLine>("l", "Uid")}={C<InventoryLedgerEntry>("e", "SourceLineId")}
                            OR {C<InboundDocumentLine>("l", "Uid")}={C<InventoryLedgerEntry>("e", "CommandItemId")}))
            ), related_facts AS (
                SELECT uid FROM direct_facts UNION
                SELECT {C<InventoryLedgerEntry>("e", "Uid")} FROM {Table<InventoryLedgerEntry>()} e
                WHERE {C<InventoryLedgerEntry>("e", "ReversalOfLedgerId")} IN(SELECT uid FROM direct_facts)
                    OR {C<InventoryLedgerEntry>("e", "ReplacementOfLedgerId")} IN(SELECT uid FROM direct_facts)
            )
            SELECT {fields} FROM {Table<InventoryLedgerEntry>()} e JOIN related_facts r ON r.uid={C<InventoryLedgerEntry>("e", "Uid")}
            ORDER BY {UuidOrder(C<InventoryLedgerEntry>("e", "Uid"))};
            """, new { Uid = uid }, token));
        return rows.AsList();
    }

    /// <summary>保留缺失与软删余额的区别，由反向服务裁决异常，不合并或修复投影。</summary>
    public Task<InventoryBalance?> ReadReverseBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token)
    {
        string[] fields = ["Uid", "WarehouseSiteId", "ProductId", "OnHandQty", "UnitSnapshot", "IsDeleted"];
        return context.Session.Connection.QuerySingleOrDefaultAsync<InventoryBalance>(Command($"""
            SELECT {string.Join(',', fields.Select(field => C<InventoryBalance>("b", field) + " AS " + PostgreSqlNames.Quote(field)))}
            FROM {Table<InventoryBalance>()} b WHERE {C<InventoryBalance>("b", "WarehouseSiteId")}=@WarehouseId
                AND {C<InventoryBalance>("b", "ProductId")}=@ProductId;
            """, new { WarehouseId = warehouseId, ProductId = productId }, token));
    }

    /// <summary>历史反向允许停用仓库，仅检查身份存在且未删除。</summary>
    public Task<bool> ReverseWarehouseExistsAsync(Guid warehouseId, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command($"""
            SELECT COUNT(*)>0 FROM {Table<WarehouseSite>()} w
            WHERE {C<WarehouseSite>("w", "Uid")}=@Uid AND {C<WarehouseSite>("w", "IsDeleted")}=FALSE;
            """, new { Uid = warehouseId }, token));

    /// <summary>透传调用方可空事务和取消令牌；不建立、提交或结束事务。</summary>
    private CommandDefinition Command(string sql, object args, CancellationToken token) => new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <summary>固定实体元数据提供 PG 表与列名，避免将 CLR 属性名直接作为物理列。</summary>
    private static string Table<T>()
    {
        var map = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
        return PostgreSqlNames.Quote(map.SchemaName) + "." + PostgreSqlNames.Quote(map.TableName);
    }

    /// <summary>仅使用本类固定字段清单构造列引用，所有业务输入均通过参数绑定。</summary>
    private static string C<T>(string alias, string property) => alias + "." + PostgreSqlNames.Quote(PostgreSqlEntityMetadata.GetMetadata(typeof(T)).GetProperty(property).ColumnName);

    /// <summary>沿用 SQL Server uniqueidentifier 的比较顺序，避免 UUID 原生顺序改变返回序列。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(index => $"get_byte(uuid_send({column}),{index})")) + "]";
}
