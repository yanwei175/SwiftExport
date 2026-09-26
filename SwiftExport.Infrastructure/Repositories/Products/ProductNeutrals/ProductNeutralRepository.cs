using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using Dapper;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.ProductNeutrals;

/// <summary>使用 Dapper 持久化中性产品，并在查询投影中解析实时颜色名称。</summary>
public sealed class ProductNeutralRepository(RepositoryBaseContext context)
    : DapperRepository<ProductNeutral>(context), IProductNeutralRepository
{
    /// <summary>按父主型号读取明细；更新场景对中性产品行申请更新锁。</summary>
    public async Task<IReadOnlyList<ProductNeutralDto>> GetByProductMasterIdAsync(Guid productMasterId, bool withUpdLock = false, CancellationToken cancellationToken = default)
    {
        var sql = $@"
SELECT pn.*, COALESCE(NULLIF(pc.ColorNameZh,''), pc.ColorNameEn, pc.ColorCode) AS ColorName
FROM dbo.ProductNeutral pn {GetLockHint(withUpdLock)}
LEFT JOIN dbo.ProductColor pc WITH(NOLOCK) ON pc.Uid=pn.ColorId AND pc.IsDeleted=0
WHERE pn.ProductMasterId=@ProductMasterId AND pn.IsDeleted=0
ORDER BY pn.ProductCode, pn.Uid;";
        var rows = await Connection.QueryAsync<ProductNeutralProjection>(
            new CommandDefinition(sql, new { ProductMasterId = productMasterId }, Transaction, cancellationToken: cancellationToken));
        return rows.Select(ToDto).ToList();
    }

    /// <summary>对规范化产品编码执行集合查询，供应用层识别当前占用者。</summary>
    public async Task<IReadOnlyList<ProductNeutral>> GetByProductCodesAsync(IEnumerable<string> productCodes, bool withUpdLock = false, CancellationToken cancellationToken = default)
    {
        var values = productCodes.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Length == 0) return [];
        var sql = $"SELECT * FROM dbo.ProductNeutral {GetLockHint(withUpdLock)} WHERE ProductCode IN @Values AND IsDeleted=0;";
        var rows = await Connection.QueryAsync<ProductNeutral>(new CommandDefinition(sql, new { Values = values }, Transaction, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    /// <summary>删除主型号时批量软删除全部子行，审计信息与父记录处于同一事务。</summary>
    public async Task<RepositoryWriteResult> SoftDeleteByProductMasterIdAsync(Guid productMasterId, CancellationToken cancellationToken = default)
    {
        var sql = @"
UPDATE dbo.ProductNeutral
SET IsDeleted=1, Version=Version+1, UpdatedDate=@UpdatedDate, UpdatedBy=@UpdatedBy
WHERE ProductMasterId=@ProductMasterId AND IsDeleted=0;";
        await Connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            ProductMasterId = productMasterId,
            UpdatedDate = DateTime.Now,
            UpdatedBy = CurrentUser.UserName
        }, Transaction, cancellationToken: cancellationToken));
        return RepositoryWriteResult.Ok(productMasterId, null, null, "中性产品已级联软删除。");
    }

    private static ProductNeutralDto ToDto(ProductNeutralProjection row) => new()
    {
        Uid = row.Uid, Version = row.Version, IsDeleted = row.IsDeleted, CreatedDate = row.CreatedDate,
        CreatedBy = row.CreatedBy, UpdatedDate = row.UpdatedDate, UpdatedBy = row.UpdatedBy,
        ProductMasterId = row.ProductMasterId, ColorName = row.ColorName,
        Data = new ProductNeutralWriteDto
        {
            ProductCode = row.ProductCode, ColorId = row.ColorId,
            DefaultSalesPriceUsd = row.DefaultSalesPriceUsd, DefaultExchangeRate = row.DefaultExchangeRate,
            ProfitMargin = row.ProfitMargin, DefaultPurchasePriceCny = row.DefaultPurchasePriceCny,
            ImagePath = row.ImagePath, IsMainImage = row.IsMainImage
        }
    };

    private sealed class ProductNeutralProjection : ProductNeutral
    {
        public string? ColorName { get; set; }
    }
}
