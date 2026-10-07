using Dapper;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>在原会话批量读取箱单过账事实，按原来源顺序锁实际需求/贡献；不承担分配业务规则或联合事务准备。</summary>
public sealed class PostgreSqlPackingListPostingFactsRepository(RepositoryBaseContext context) : IPackingListPostingFactsRepository
{
    /// <summary>保持原完整关联和软删范围，按各阶段实际结果继续读取，不将坏引用隐藏为无需求。</summary>
    public async Task<PackingListPostingFacts> ReadAsync(Guid packingListId, CancellationToken token)
    {
        var facts = new PackingListPostingFacts();
        facts.Head = (await Read<PackingListHead>("uid=@Id", new { Id = packingListId }, token)).SingleOrDefault();
        if (facts.Head is null) return facts;
        facts.Lines = await Read<PackingListLine>("packing_list_head_id=@Id AND is_deleted=FALSE", new { Id = packingListId }, token);
        var ids = facts.Lines.Select(x => x.GrossDemandId).OfType<Guid>().Distinct().Order().ToArray();
        facts.Sources = await Read<GrossDemand>("uid=ANY(@Ids)", Ids(ids), token);
        facts.Demands = await Read<NetDemand>("gross_demand_id=ANY(@Ids) AND is_deleted=FALSE", Ids(ids), token);
        facts.Links = await Read<DemandExecutionLink>("net_demand_id=ANY(@Ids) AND is_deleted=FALSE", Ids(facts.Demands.Select(x => x.Uid).ToArray()), token);
        facts.PurchaseLines = await Read<PurchaseOrderLine>("uid=ANY(@Ids)", Ids(facts.Links.Where(x => x.TargetType == "PO").Select(x => x.TargetLineId).Distinct().ToArray()), token);
        facts.PurchaseHeads = await Read<PurchaseOrderHead>("uid=ANY(@Ids)", Ids(facts.PurchaseLines.Select(x => x.PurchaseOrderId).Distinct().ToArray()), token);
        var products = facts.Sources.Select(x => x.ProductId).Concat(facts.Demands.Select(x => x.ProductId)).OfType<Guid>().Distinct().Order().ToArray();
        facts.Products = await Read<Product>("uid=ANY(@Ids)", Ids(products), token);
        facts.Warehouses = await Read<WarehouseSite>("is_deleted=FALSE AND is_active=TRUE", null, token);
        var balances = Ids(products);
        balances.AddIds("Warehouses", facts.Warehouses.Select(x => x.Uid).ToArray());
        facts.Balances = await Read<InventoryBalance>("product_id=ANY(@Ids) AND warehouse_site_id=ANY(@Warehouses)", balances, token);
        var delivered = await context.Session.Connection.QueryAsync<DeliveredRow>(Command("""
            SELECT l.gross_demand_id AS "GrossDemandId",SUM(l.qty) AS "Qty"
            FROM public.packing_list_line l JOIN public.packing_list_head h ON h.uid=l.packing_list_head_id
            WHERE l.gross_demand_id=ANY(@Ids) AND l.is_deleted=FALSE AND h.is_deleted=FALSE AND h.posting_status=1
            GROUP BY l.gross_demand_id;
            """, Ids(ids), token));
        facts.Delivered = delivered.ToDictionary(x => x.GrossDemandId, x => x.Qty);
        facts.CustomerExists = await context.Session.Connection.ExecuteScalarAsync<bool>(Command("SELECT EXISTS(SELECT 1 FROM public.customer WHERE uid=@Id AND is_deleted=FALSE)", new { Id = facts.Head.CustomerId }, token));
        return facts;
    }

    /// <summary>保留原事务门禁及来源→需求→贡献顺序，父来源为现有子集合锚点，缺失范围和高隔离联合协议另验。</summary>
    public async Task LockSourcesAsync(IReadOnlyList<Guid> grossDemandIds, CancellationToken token)
    {
        if (context.Session.Transaction is null) throw new InvalidOperationException("来源锁必须加入调用方事务。");
        foreach (var id in grossDemandIds.Distinct().Order())
            await context.Session.Connection.QueryAsync<Guid>(Command("SELECT uid FROM public.gross_demand WHERE uid=@Id FOR UPDATE", new { Id = id }, token));
        var demandIds = new List<Guid>();
        foreach (var id in grossDemandIds.Distinct().Order())
            demandIds.AddRange(await context.Session.Connection.QueryAsync<Guid>(Command($"SELECT uid FROM public.net_demand WHERE gross_demand_id=@Id ORDER BY {UuidOrder("uid")} FOR UPDATE", new { Id = id }, token)));
        foreach (var id in demandIds.Distinct().Order())
            await context.Session.Connection.QueryAsync<Guid>(Command($"SELECT uid FROM public.demand_execution_link WHERE net_demand_id=@Id ORDER BY {UuidOrder("uid")} FOR UPDATE", new { Id = id }, token));
    }

    /// <summary>固定谓词与已有实体映射提供物理列及CLR别名，保留原Qty别名和SqlGuid顺序，不接受外部标识符。</summary>
    private async Task<IReadOnlyList<T>> Read<T>(string where, object? parameters, CancellationToken token)
    {
        var map = EntityMetadataCache.GetMetadata(typeof(T));
        var columns = string.Join(',', map.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName) + " AS " + PostgreSqlNames.Quote(c.PropertyName)));
        return (await context.Session.Connection.QueryAsync<T>(Command($"SELECT {columns} FROM {PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)} WHERE {where} ORDER BY {UuidOrder("uid")}", parameters, token))).ToArray();
    }
    /// <summary>数组包括空集合显式绑定uuid[]，避免Dapper展开或推测类型。</summary>
    private static PostgreSqlParameters Ids(Guid[] values)
    {
        var parameters = new PostgreSqlParameters();
        parameters.AddIds("Ids", values);
        return parameters;
    }

    /// <summary>保持源uniqueidentifier字节优先级。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(i => $"get_byte(uuid_send({column}),{i})")) + "]";

    /// <summary>透传当前可空父事务及取消令牌，不创建连接或改变事务所有权。</summary>
    private CommandDefinition Command(string sql, object? parameters, CancellationToken token) => new(sql, parameters, context.Session.Transaction, cancellationToken: token);

    /// <summary>SQL实际返回的权威交付净额内部投影。</summary>
    private sealed class DeliveredRow
    {
        public Guid GrossDemandId { get; set; }
        public decimal Qty { get; set; }
    }
}
