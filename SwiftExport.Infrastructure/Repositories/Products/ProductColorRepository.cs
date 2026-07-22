using Dapper;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Products;

public class ProductColorRepository(RepositoryBaseContext context) : DapperRepository<ProductColor>(context), IProductColorRepository
{
    private static readonly string ProductColorTable = EntityMetadataCache.GetTableName<ProductColor>();
    private static readonly string ProductTable = EntityMetadataCache.GetTableName<Product>();

    public async Task<bool> ExistByColorCode(string productColor)
    {
        // 1. SQL 修正：查询最小数据（例如数字 1）并使用 = 替代 IN
        var sql = $"SELECT 1 FROM [{TableName}] WHERE ColorCodeUnique = @Code";
        int? result = await Connection.QueryFirstOrDefaultAsync<int?>(sql, new { Code = productColor }, transaction: Transaction);

        // 4. 逻辑修正：如果 result 有值（即不是 null），则表示存在。
        return result.HasValue;
    }

    public async Task<IReadOnlyList<string>> GetExistingColorCodesAsync(IEnumerable<string> productColorList)
    {
        var sql = $"SELECT ColorCodeUnique FROM [{TableName}] WHERE  ColorCodeUnique IN @Codes";
        var rst = await Connection.QueryAsync<string>(sql, new { Codes = productColorList }, transaction: Transaction);
        return [.. rst];
    }

    public async Task<IReadOnlyList<ProductColor>> GetByColorCodesAsync(
        IEnumerable<string> colorCodes,
        bool withUpdLock = false)
    {
        var normalized = colorCodes?
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (normalized.Length == 0) return [];

        var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";
        var sql = $@"
SELECT
    [{nameof(ProductColor.Uid)}],
    [{nameof(ProductColor.Version)}],
    [{nameof(ProductColor.IsDeleted)}],
    [{nameof(ProductColor.ColorCodeUnique)}],
    [{nameof(ProductColor.ColorCode)}],
    [{nameof(ProductColor.ColorNameEn)}],
    [{nameof(ProductColor.ColorNameZh)}]
FROM [{ProductColorTable}] {lockHint}
WHERE [{nameof(ProductColor.ColorCodeUnique)}] IN @Codes
  AND [{nameof(ProductColor.IsDeleted)}] = 0;";
        var items = await Connection.QueryAsync<ProductColor>(
            sql,
            new { Codes = normalized },
            transaction: Transaction);
        return [.. items];
    }

    public async Task<IReadOnlyList<Guid>> GetReferencedColorIdsAsync(IEnumerable<Guid> colorIds)
    {
        var normalized = colorIds?
            .Where(uid => uid != Guid.Empty)
            .Distinct()
            .ToArray() ?? [];
        if (normalized.Length == 0) return [];

        var sql = $@"
SELECT DISTINCT [{nameof(Product.ColorId)}]
FROM [{ProductTable}] WITH (UPDLOCK, ROWLOCK)
WHERE [{nameof(Product.ColorId)}] IN @ColorIds
  AND [{nameof(Product.IsDeleted)}] = 0;";
        var ids = await Connection.QueryAsync<Guid>(
            sql,
            new { ColorIds = normalized },
            transaction: Transaction);
        return [.. ids];
    }

    public async Task<PagedResponseBase<ProductColor>> GetProductColorPagedAsync(ProductColorSearchRequest request)
    {
        request ??= new ProductColorSearchRequest();

        var pageIndex = request.PageIndex > 0 ? request.PageIndex : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 50;
        var offset = (pageIndex - 1) * pageSize;

        var parameters = new DynamicParameters();
        parameters.Add("@Offset", offset);
        parameters.Add("@Limit", pageSize);

        var where = new StringBuilder("WHERE (IsDeleted = 0 OR IsDeleted IS NULL)");

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("@Keyword", SqlLikePattern.Build(request.Keyword));
            where.Append(@"
                    AND (
                           ColorCodeUnique LIKE @Keyword ESCAPE '~'
                        OR ColorCode LIKE @Keyword ESCAPE '~'
                        OR ColorNameEn LIKE @Keyword ESCAPE '~'
                        OR ColorNameZh LIKE @Keyword ESCAPE '~'
                    )");
        }

        var batchKeywords = request.BatchKeywords?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (batchKeywords is { Count: > 0 })
        {
            parameters.Add("@BatchKeywords", batchKeywords);
            where.Append(@"
                    AND (
                           ColorCodeUnique IN @BatchKeywords
                        OR ColorCode IN @BatchKeywords
                    )");
        }

        var sql = $@"
                SELECT COUNT(1)
                FROM [{ProductColorTable}] WITH(NOLOCK)
                {where};

                SELECT
                    Uid,
                    Version,
                    IsDeleted,
                    CreatedDate,
                    CreatedBy,
                    UpdatedDate,
                    UpdatedBy,
                    ColorCodeUnique,
                    ColorCode,
                    ColorNameEn,
                    ColorNameZh
                FROM [{ProductColorTable}] WITH(NOLOCK)
                {where}
                ORDER BY ColorCodeUnique ASC, Uid ASC
                OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

        using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
        var total = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<ProductColor>();

        return new PagedResponseBase<ProductColor>
        {
            Items = [.. items],
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }
}
