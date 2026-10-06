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
    private static readonly string[] ReverseLedgerFields = ["Uid", "CommandId", "CommandItemId", "WarehouseSiteId", "ProductId", "QuantityDelta", "UnitSnapshot", "SkuSnapshot", "TransactionType", "SourceDocumentType", "SourceDocumentId", "SourceLineId", "ReversalOfLedgerId", "ReplacementOfLedgerId", "CorrectionGroupId", "IsDeleted"];

    /// <summary>只读汇总同仓同产品未删余额，无记录返回零，不预占库存。</summary>
    public Task<decimal> ReadBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<decimal>(Command($"""
            SELECT COALESCE(SUM({C<InventoryBalance>("b", "OnHandQty")}),0) FROM {Table<InventoryBalance>()} b
            WHERE {C<InventoryBalance>("b", "WarehouseSiteId")}=@WarehouseId AND {C<InventoryBalance>("b", "ProductId")}=@ProductId
                AND {C<InventoryBalance>("b", "IsDeleted")}=FALSE;
            """, new { WarehouseId = warehouseId, ProductId = productId }, token));

    /// <summary>读取有效且启用仓库的名称，缺失或停用返回空。</summary>
    public Task<string?> ReadActiveWarehouseNameAsync(Guid warehouseId, CancellationToken token) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<string>(Command($"""
            SELECT {C<WarehouseSite>("w", "Name")} FROM {Table<WarehouseSite>()} w
            WHERE {C<WarehouseSite>("w", "Uid")}=@WarehouseId AND {C<WarehouseSite>("w", "IsActive")}=TRUE
                AND {C<WarehouseSite>("w", "IsDeleted")}=FALSE;
            """, new { WarehouseId = warehouseId }, token));

    /// <summary>读取原单及一次关联的反向、替代流水，保留删除行与历史异常事实供服务判断。</summary>
    public async Task<IReadOnlyList<InventoryLedgerEntry>> ReadReverseLedgersAsync(Guid uid, CancellationToken token)
    {
        var fields = string.Join(',', ReverseLedgerFields.Select(field => C<InventoryLedgerEntry>("e", field) + " AS " + PostgreSqlNames.Quote(field)));
        var rows = await context.Session.Connection.QueryAsync<InventoryLedgerEntry>(Command($"""
            WITH direct_facts AS (
                SELECT {C<InventoryLedgerEntry>("e", "Uid")} AS uid FROM {Table<InventoryLedgerEntry>()} e
                WHERE {C<InventoryLedgerEntry>("e", "SourceDocumentId")}=@Uid OR EXISTS(
                    SELECT 1 FROM {Table<OutboundDocumentLine>()} l WHERE {C<OutboundDocumentLine>("l", "OutboundDocumentId")}=@Uid
                        AND ({C<OutboundDocumentLine>("l", "Uid")}={C<InventoryLedgerEntry>("e", "SourceLineId")}
                            OR {C<OutboundDocumentLine>("l", "CommandItemId")}={C<InventoryLedgerEntry>("e", "CommandItemId")}))
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

    /// <summary>读取包含软删状态的原余额，缺失不补建，异常由反向服务判断。</summary>
    public Task<InventoryBalance?> ReadReverseBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token)
    {
        string[] fields = ["Uid", "WarehouseSiteId", "ProductId", "OnHandQty", "UnitSnapshot", "IsDeleted"];
        return context.Session.Connection.QuerySingleOrDefaultAsync<InventoryBalance>(Command($"""
            SELECT {string.Join(',', fields.Select(field => C<InventoryBalance>("b", field) + " AS " + PostgreSqlNames.Quote(field)))}
            FROM {Table<InventoryBalance>()} b WHERE {C<InventoryBalance>("b", "WarehouseSiteId")}=@WarehouseId
                AND {C<InventoryBalance>("b", "ProductId")}=@ProductId;
            """, new { WarehouseId = warehouseId, ProductId = productId }, token));
    }

    /// <summary>允许停用仓库用于历史反向，仅要求身份存在且未删除。</summary>
    public Task<bool> ReverseWarehouseExistsAsync(Guid warehouseId, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command($"""
            SELECT COUNT(*)>0 FROM {Table<WarehouseSite>()} w
            WHERE {C<WarehouseSite>("w", "Uid")}=@Uid AND {C<WarehouseSite>("w", "IsDeleted")}=FALSE;
            """, new { Uid = warehouseId }, token));

    /// <summary>只透传当前事务和取消，不开始或结束业务事务。</summary>
    private CommandDefinition Command(string sql, object args, CancellationToken token) => new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <summary>从固定实体元数据取得 PG 表名，避免手写表名与生成结构分叉。</summary>
    private static string Table<T>()
    {
        var map = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
        return PostgreSqlNames.Quote(map.SchemaName) + "." + PostgreSqlNames.Quote(map.TableName);
    }

    /// <summary>将固定 CLR 属性映射为转义的 PG 列名，业务身份始终参数化。</summary>
    private static string C<T>(string alias, string property) => alias + "." + PostgreSqlNames.Quote(PostgreSqlEntityMetadata.GetMetadata(typeof(T)).GetProperty(property).ColumnName);

    /// <summary>保留原 uniqueidentifier 排序，不以 PG UUID 原生顺序替代。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(index => $"get_byte(uuid_send({column}),{index})")) + "]";
}
