using Dapper;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.ReferenceRead;

/// <summary>采购创建与确认共用的主数据资格读取；依赖调用方既有事务。</summary>
public sealed class ProcurementReferenceReadRepository(RepositoryBaseContext context) : IProcurementReferenceReadRepository
{
    private CommandDefinition Command(string sql, object? args, CancellationToken ct)
        => new(sql, args, context.Session.Transaction ?? throw new InvalidOperationException("采购持久化需要事务。"), cancellationToken: ct);

    /// <inheritdoc/>
    public Task<Customer?> GetCustomerAsync(Guid uid, CancellationToken ct)
        => context.Session.Connection.QuerySingleOrDefaultAsync<Customer>(Command(
            "SELECT Uid,Version,IsActive,CustomerCode,CustomerName FROM dbo.Customer WITH(HOLDLOCK) WHERE Uid=@uid AND ISNULL(IsDeleted,0)=0;", new { uid }, ct));
    /// <inheritdoc/>
    public Task<Supplier?> GetSupplierAsync(Guid uid, CancellationToken ct)
        => context.Session.Connection.QuerySingleOrDefaultAsync<Supplier>(Command(
            "SELECT Uid,Version,IsActive,SupplierCode,SupplierName FROM dbo.Supplier WITH(HOLDLOCK) WHERE Uid=@uid AND ISNULL(IsDeleted,0)=0;", new { uid }, ct));
    /// <inheritdoc/>
    public Task<Product?> GetProductAsync(Guid uid, CancellationToken ct)
        => context.Session.Connection.QuerySingleOrDefaultAsync<Product>(Command(
            "SELECT Uid,Version,IsActive,Sku,DescriptionZh,Unit,MaterialType,SupplyType,BomNo,BomVersion FROM dbo.Product WITH(HOLDLOCK) WHERE Uid=@uid AND ISNULL(IsDeleted,0)=0;", new { uid }, ct));
    /// <inheritdoc/>
    public Task<ProcurementProductCandidateDto?> GetProductCandidateAsync(Guid productId, Guid customerId, Guid supplierId, CancellationToken ct)
        => context.Session.Connection.QuerySingleOrDefaultAsync<ProcurementProductCandidateDto>(Command("""
            SELECT p.Uid,p.Sku,COALESCE(p.MainImage,N'') MainImage,COALESCE(cp.CustomerSku,N'') CustomerSku,
                   COALESCE(p.LinkSku,N'') LinkSku,COALESCE(sp.SupplierSku,N'') SupplierSku,
                   COALESCE(p.DescriptionZh,N'') DescriptionZh,COALESCE(p.DescriptionEn,N'') DescriptionEn,
                   COALESCE(color.ColorNameZh,N'') FinishZh,COALESCE(p.FeaturesZh,N'') FeaturesZh,
                   COALESCE(p.MaterialType,N'') MaterialType,COALESCE(p.SupplyType,N'') SupplyType,COALESCE(p.Unit,N'') Unit
            FROM dbo.Product p WITH(HOLDLOCK)
            LEFT JOIN dbo.ProductColor color WITH(HOLDLOCK) ON color.Uid=p.ColorId AND ISNULL(color.IsDeleted,0)=0
            OUTER APPLY(SELECT TOP(1) r.CustomerSku FROM dbo.CustomerProduct r WITH(HOLDLOCK)
              WHERE r.CustomerId=@customerId AND r.SkuId=p.Uid AND r.IsActive=1 AND ISNULL(r.IsDeleted,0)=0 ORDER BY r.Uid) cp
            OUTER APPLY(SELECT TOP(1) r.SupplierSku FROM dbo.SupplierProduct r WITH(HOLDLOCK)
              WHERE r.SupplierId=@supplierId AND r.SkuId=p.Uid AND ISNULL(r.IsDeleted,0)=0 ORDER BY r.Uid) sp
            WHERE p.Uid=@productId AND p.IsActive=1 AND ISNULL(p.IsDeleted,0)=0;
            """, new { productId, customerId, supplierId }, ct));
    /// <inheritdoc/>
    public async Task<string?> GetUnitAsync(string unit, CancellationToken ct)
    {
        var values = (await context.Session.Connection.QueryAsync<string>(Command("""
            SELECT LTRIM(RTRIM(DataNameEn)) FROM dbo.SysDictData WITH(HOLDLOCK)
            WHERE DictCode=N'Unit' AND IsEnabled=1 AND ISNULL(IsDeleted,0)=0
              AND UPPER(LTRIM(RTRIM(DataNameEn)))=UPPER(@unit);
            """, new { unit }, ct))).ToList();
        return values.Count == 1 ? values[0] : null;
    }
}
