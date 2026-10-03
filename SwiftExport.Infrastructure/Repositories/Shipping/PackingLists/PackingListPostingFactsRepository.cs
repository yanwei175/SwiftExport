using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Dapper;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>在当前 DbSession 批量读取箱单过账事实；SQL 不承担分配业务规则。</summary>
public sealed class PackingListPostingFactsRepository(RepositoryBaseContext context) : IPackingListPostingFactsRepository
{
    /// <inheritdoc/>
    public async Task<PackingListPostingFacts> ReadAsync(Guid packingListId, CancellationToken token)
    {
        var facts = new PackingListPostingFacts();
        facts.Head = (await Read<PackingListHead>("Uid=@Id", new { Id = packingListId }, token)).SingleOrDefault();
        if (facts.Head is null) return facts;
        facts.Lines = await Read<PackingListLine>("PackingListHeadId=@Id AND IsDeleted=0", new { Id = packingListId }, token);
        var ids = facts.Lines.Select(x => x.GrossDemandId).OfType<Guid>().Distinct().Order().ToArray();
        facts.Sources = await Read<GrossDemand>("Uid IN @Ids", new { Ids = ids }, token);
        facts.Demands = await Read<NetDemand>("GrossDemandId IN @Ids AND IsDeleted=0", new { Ids = ids }, token);
        facts.Links = await Read<DemandExecutionLink>("NetDemandId IN @Ids AND IsDeleted=0",
            new { Ids = facts.Demands.Select(x => x.Uid).ToArray() }, token);
        facts.PurchaseLines = await Read<PurchaseOrderLine>("Uid IN @Ids",
            new { Ids = facts.Links.Where(x => x.TargetType == "PO").Select(x => x.TargetLineId).Distinct().ToArray() }, token);
        facts.PurchaseHeads = await Read<PurchaseOrderHead>("Uid IN @Ids",
            new { Ids = facts.PurchaseLines.Select(x => x.PurchaseOrderId).Distinct().ToArray() }, token);
        var products = facts.Sources.Select(x => x.ProductId).Concat(facts.Demands.Select(x => x.ProductId))
            .OfType<Guid>().Distinct().Order().ToArray();
        facts.Products = await Read<Product>("Uid IN @Ids", new { Ids = products }, token);
        facts.Warehouses = await Read<WarehouseSite>("IsDeleted=0 AND IsActive=1", null, token);
        facts.Balances = await Read<InventoryBalance>("ProductId IN @Ids AND WarehouseSiteId IN @Warehouses",
            new { Ids = products, Warehouses = facts.Warehouses.Select(x => x.Uid).ToArray() }, token);
        var delivered = await context.Session.Connection.QueryAsync<DeliveredRow>(Command("""
            SELECT l.GrossDemandId, SUM(l.Qty) AS Qty
            FROM dbo.PackingListLine l
            INNER JOIN dbo.PackingListHead h ON h.Uid=l.PackingListHeadId
            WHERE l.GrossDemandId IN @Ids AND l.IsDeleted=0 AND h.IsDeleted=0 AND h.PostingStatus=1
            GROUP BY l.GrossDemandId;
            """, new { Ids = ids }, token));
        facts.Delivered = delivered.ToDictionary(x => x.GrossDemandId, x => x.Qty);
        facts.CustomerExists = await context.Session.Connection.ExecuteScalarAsync<bool>(Command(
            "SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.Customer WHERE Uid=@Id AND IsDeleted=0) THEN 1 ELSE 0 END);",
            new { Id = facts.Head.CustomerId }, token));
        return facts;
    }

    /// <inheritdoc/>
    public async Task LockSourcesAsync(IReadOnlyList<Guid> grossDemandIds, CancellationToken token)
    {
        if (context.Session.Transaction is null) throw new InvalidOperationException("来源锁必须加入调用方事务。");
        foreach (var id in grossDemandIds.Distinct().Order())
            await context.Session.Connection.QueryAsync<Guid>(Command(
                "SELECT Uid FROM dbo.GrossDemand WITH (UPDLOCK,HOLDLOCK) WHERE Uid=@Id;", new { Id = id }, token));
        var demandIds = new List<Guid>();
        foreach (var id in grossDemandIds.Distinct().Order())
            demandIds.AddRange(await context.Session.Connection.QueryAsync<Guid>(Command(
                "SELECT Uid FROM dbo.NetDemand WITH (UPDLOCK,HOLDLOCK) WHERE GrossDemandId=@Id ORDER BY Uid;", new { Id = id }, token)));
        foreach (var id in demandIds.Distinct().Order())
            await context.Session.Connection.QueryAsync<Guid>(Command(
                "SELECT Uid FROM dbo.DemandExecutionLink WITH (UPDLOCK,HOLDLOCK) WHERE NetDemandId=@Id ORDER BY Uid;", new { Id = id }, token));
    }

    /// <summary>列清单只从编译期实体元数据产生；显式处理 QTY 别名，where 仅允许本类固定 SQL。</summary>
    private async Task<IReadOnlyList<T>> Read<T>(string where, object? args, CancellationToken token)
    {
        var table = typeof(T).GetCustomAttribute<TableAttribute>()!.Name;
        var columns = typeof(T).GetProperties().Where(x => x.CanRead && x.CanWrite && x.GetCustomAttribute<NotMappedAttribute>() == null)
            .Select(x => $"[{x.GetCustomAttribute<ColumnAttribute>()?.Name ?? x.Name}] AS [{x.Name}]");
        return (await context.Session.Connection.QueryAsync<T>(Command(
            $"SELECT {string.Join(',', columns)} FROM dbo.[{table}] WHERE {where} ORDER BY Uid;", args, token))).ToArray();
    }

    /// <summary>所有 SQL 使用当前连接事务及调用方取消令牌。</summary>
    private CommandDefinition Command(string sql, object? args, CancellationToken token) =>
        new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <summary>交付净额查询的内部映射，不对外暴露。</summary>
    private sealed class DeliveredRow
    {
        public Guid GrossDemandId { get; set; }
        public decimal Qty { get; set; }
    }
}
