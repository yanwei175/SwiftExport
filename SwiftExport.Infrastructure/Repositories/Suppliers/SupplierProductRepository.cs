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
        sp.UnitPrice, sp.TaxPrice, s.Currency, s.TaxRate
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
                       sp.SupplierSku LIKE @Keyword
                    OR p.Sku LIKE @Keyword
                    OR p.LinkSku LIKE @Keyword
                    OR sp.SupplierSkuDescriptionZh LIKE @Keyword
                    OR sp.SupplierSkuFeaturesZh LIKE @Keyword
                )
                """);
            parameters.Add("Keyword", $"%{request.Keyword.Trim()}%");
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
}
