using Dapper;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Core.Interfaces.ForExcel;
using SwiftExport.Infrastructure.Contexts;
namespace SwiftExport.Infrastructure.Repositories.ForExcel;

/// <summary>采购导入精确匹配查询；不写库，正式创建时共享可串行化事务与范围锁。</summary>
public sealed class ForExcelPurchaseOrderRepository(RepositoryBaseContext context) : IForExcelPurchaseOrderRepository
{
    /// <summary>沿用请求数据库会话及取消，不另建连接或事务。</summary>
    private CommandDefinition Command(string sql, object args, CancellationToken token)
        => new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PurchaseOrderImportCustomerDto>> FindCustomersAsync(string code, CancellationToken token)
        => (await context.Session.Connection.QueryAsync<PurchaseOrderImportCustomerDto>(Command(
            "SELECT Uid,CustomerCode,CustomerName,IsActive FROM dbo.Customer WITH(HOLDLOCK) WHERE IsDeleted=0 AND UPPER(LTRIM(RTRIM(CustomerCode)))=@Code ORDER BY Uid;",
            new { Code = code.Trim().ToUpperInvariant() }, token))).AsList();

    /// <inheritdoc />
    public Task<PurchaseOrderImportSupplierDto?> FindSupplierAsync(Guid uid, CancellationToken token)
        => context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderImportSupplierDto>(Command(
            "SELECT Uid,SupplierCode,SupplierName,IsActive FROM dbo.Supplier WITH(HOLDLOCK) WHERE Uid=@uid AND IsDeleted=0;",
            new { uid }, token));

    /// <inheritdoc />
    public async Task<IReadOnlyList<PurchaseOrderImportProductCandidateDto>> MatchProductsAsync(Guid customerId, string[] skus, CancellationToken token)
    {
        if (skus.Length == 0) return [];
        return (await context.Session.Connection.QueryAsync<PurchaseOrderImportProductCandidateDto>(Command("""
            SELECT cp.Uid AS CustomerProductId,cp.CustomerSku,p.Uid AS ProductId,p.Sku,
                   COALESCE(NULLIF(LTRIM(RTRIM(cp.LinkSku)),''),p.LinkSku) AS LinkSku,
                   p.Unit,p.MainImage,p.MaterialType,
                   CAST(CASE WHEN cp.IsActive=1 AND p.IsActive=1 AND p.SupplyType=N'采购'
                     AND p.MaterialType IN(N'成品',N'零件',N'客供件')
                     AND NULLIF(LTRIM(RTRIM(p.Sku)),'') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS IsUsable
            FROM dbo.CustomerProduct cp WITH(HOLDLOCK)
            LEFT JOIN dbo.Product p WITH(HOLDLOCK) ON p.Uid=cp.SkuId AND p.IsDeleted=0
            WHERE cp.CustomerId=@CustomerId AND cp.IsDeleted=0
              AND UPPER(LTRIM(RTRIM(cp.CustomerSku))) IN @Skus
            ORDER BY cp.CustomerSku,cp.Uid;
            """, new { CustomerId = customerId, Skus = skus }, token))).AsList();
    }

    /// <inheritdoc />
    public async Task<string?> FindUnitAsync(string unit, CancellationToken token)
    {
        var items = (await context.Session.Connection.QueryAsync<string>(Command("""
            SELECT LTRIM(RTRIM(DataNameEn)) FROM dbo.SysDictData WITH(HOLDLOCK)
            WHERE DictCode=N'Unit' AND IsEnabled=1 AND IsDeleted=0
              AND UPPER(LTRIM(RTRIM(DataNameEn)))=@Unit ORDER BY Uid;
            """, new { Unit = unit.Trim().ToUpperInvariant() }, token))).AsList();
        return items.Count == 1 ? items[0] : null;
    }
}
