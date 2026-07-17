using Dapper;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Suppliers;

public sealed class SupplierProductRepository(RepositoryBaseContext context)
    : DapperRepository<SupplierProduct>(context), ISupplierProductRepository
{
    private const string SelectColumns = """
        sp.Uid, sp.Version, sp.IsDeleted,
        sp.CreatedDate, sp.CreatedBy, sp.UpdatedDate, sp.UpdatedBy,
        sp.SupplierId, s.SupplierCode, s.SupplierName,
        sp.SkuId, p.Sku AS ProductSku, p.LinkSku,
        p.DescriptionEn, p.DescriptionZh, p.FeaturesZh,
        pc.CategoryName, p.MaterialType, p.MainImage,
        sp.SupplierSku, sp.SupplierSkuDescriptionZh, sp.SupplierSkuFeaturesZh,
        sp.UnitPrice, sp.TaxPrice, s.Currency, s.TaxRate,
        CASE WHEN p.DefaultSupplierId = sp.SupplierId THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsProductDefaultSupplier
        """;

    private const string FromAndJoins = """
        FROM dbo.SupplierProduct sp WITH (NOLOCK)
        INNER JOIN dbo.Supplier s WITH (NOLOCK)
            ON s.Uid = sp.SupplierId AND ISNULL(s.IsDeleted, 0) = 0
        INNER JOIN dbo.Product p WITH (NOLOCK)
            ON p.Uid = sp.SkuId AND ISNULL(p.IsDeleted, 0) = 0
        LEFT JOIN dbo.ProductCategory pc WITH (NOLOCK)
            ON pc.Uid = p.CategoryId AND ISNULL(pc.IsDeleted, 0) = 0
        """;

    public async Task<SupplierProductDto?> GetDtoByIdAsync(Guid uid)
    {
        var sql = $"""
            SELECT {SelectColumns}
            {FromAndJoins}
            WHERE sp.Uid = @Uid AND ISNULL(sp.IsDeleted, 0) = 0;
            """;
        return await Connection.QueryFirstOrDefaultAsync<SupplierProductDto>(
            sql, new { Uid = uid }, transaction: Transaction);
    }

    public async Task<PagedResponseBase<SupplierProductDto>> GetPagedAsync(SupplierProductSearchRequest request)
    {
        request ??= new SupplierProductSearchRequest();
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Min(request.PageSize <= 0 ? 20 : request.PageSize, 200);
        var parameters = new DynamicParameters(new
        {
            request.SupplierId,
            Offset = (pageIndex - 1) * pageSize,
            PageSize = pageSize,
        });
        var where = new StringBuilder();
        where.AppendLine("WHERE ISNULL(sp.IsDeleted, 0) = 0 AND sp.SupplierId = @SupplierId");
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            where.AppendLine("""
                AND (
                       sp.SupplierSku LIKE @Keyword ESCAPE '~'
                    OR p.Sku LIKE @Keyword ESCAPE '~'
                    OR p.LinkSku LIKE @Keyword ESCAPE '~'
                    OR sp.SupplierSkuDescriptionZh LIKE @Keyword ESCAPE '~'
                    OR sp.SupplierSkuFeaturesZh LIKE @Keyword ESCAPE '~'
                )
                """);
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
        }

        var sql = $"""
            SELECT COUNT(1)
            {FromAndJoins}
            {where};

            SELECT {SelectColumns}
            {FromAndJoins}
            {where}
            ORDER BY sp.SupplierSku ASC, sp.Uid ASC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
        var totalCount = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<SupplierProductDto>();
        return new PagedResponseBase<SupplierProductDto>
        {
            Items = items.ToList(),
            TotalCount = totalCount,
            PageIndex = pageIndex,
            PageSize = pageSize,
        };
    }

    public async Task<IReadOnlyList<string>> GetExistingSupplierSkuListAsync(
        IEnumerable<string> supplierSkus,
        Guid supplierId)
    {
        const string sql = """
            SELECT SupplierSku
            FROM dbo.SupplierProduct
            WHERE SupplierId = @SupplierId
              AND SupplierSku IN @SupplierSkus
              AND ISNULL(IsDeleted, 0) = 0;
            """;
        return (await Connection.QueryAsync<string>(sql, new
        {
            SupplierId = supplierId,
            SupplierSkus = supplierSkus,
        }, transaction: Transaction)).ToList();
    }

    public async Task<IReadOnlyList<string>> GetSupplierSkusUsedByOtherRecordsAsync(
        Guid supplierId,
        IEnumerable<(Guid Uid, string SupplierSku)> items)
    {
        var materialized = items.ToArray();
        if (materialized.Length == 0) return [];
        const string sql = """
            SELECT DISTINCT SupplierSku
            FROM dbo.SupplierProduct
            WHERE SupplierId = @SupplierId
              AND SupplierSku IN @SupplierSkus
              AND Uid NOT IN @Uids
              AND ISNULL(IsDeleted, 0) = 0;
            """;
        return (await Connection.QueryAsync<string>(sql, new
        {
            SupplierId = supplierId,
            SupplierSkus = materialized.Select(item => item.SupplierSku),
            Uids = materialized.Select(item => item.Uid),
        }, transaction: Transaction)).ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetSupplierIdsWithProductsAsync(IEnumerable<Guid> supplierIds)
    {
        var ids = supplierIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        const string sql = """
            SELECT DISTINCT SupplierId
            FROM dbo.SupplierProduct
            WHERE SupplierId IN @SupplierIds
              AND ISNULL(IsDeleted, 0) = 0;
            """;
        return (await Connection.QueryAsync<Guid>(
            sql, new { SupplierIds = ids }, transaction: Transaction)).ToList();
    }

    public async Task<IReadOnlyList<SupplierProduct>> GetPurchaseDataAsync(
        IEnumerable<(Guid ProductId, Guid SupplierId)> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var materialized = keys
            .Where(x => x.ProductId != Guid.Empty && x.SupplierId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (materialized.Length == 0) return [];

        var result = new List<SupplierProduct>();
        foreach (var chunk in materialized.Chunk(500))
        {
            var parameters = new DynamicParameters();
            var values = new string[chunk.Length];
            for (var index = 0; index < chunk.Length; index++)
            {
                var productParameter = $"ProductId{index}";
                var supplierParameter = $"SupplierId{index}";
                values[index] = $"(@{productParameter},@{supplierParameter})";
                parameters.Add(productParameter, chunk[index].ProductId);
                parameters.Add(supplierParameter, chunk[index].SupplierId);
            }

            var sql = $"""
                SELECT sp.Uid,sp.Version,sp.IsDeleted,sp.CreatedDate,sp.CreatedBy,sp.UpdatedDate,sp.UpdatedBy,
                       sp.SupplierId,sp.SupplierCode,sp.SkuId,sp.ProductSku,sp.SupplierSku,
                       sp.SupplierSkuDescriptionZh,sp.SupplierSkuFeaturesZh,sp.UnitPrice,sp.TaxPrice
                FROM dbo.SupplierProduct sp
                INNER JOIN (VALUES {string.Join(',', values)}) requested(ProductId,SupplierId)
                        ON requested.ProductId=sp.SkuId AND requested.SupplierId=sp.SupplierId
                WHERE ISNULL(sp.IsDeleted,0)=0
                ORDER BY sp.SkuId,sp.SupplierId,sp.Uid;
                """;
            result.AddRange(await Connection.QueryAsync<SupplierProduct>(
                sql, parameters, transaction: Transaction));
        }

        return result
            .OrderBy(x => x.SkuId)
            .ThenBy(x => x.SupplierId)
            .ThenBy(x => x.Uid)
            .ToList();
    }
}
