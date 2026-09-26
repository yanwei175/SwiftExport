using SwiftExport.Core.Interfaces.Products.Categories;
using Dapper;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Products.Categories;

public class ProductCategoryRepository(RepositoryBaseContext context)
    : DapperRepository<ProductCategory>(context), IProductCategoryRepository
{
    private static readonly string ProductCategoryTable = EntityMetadataCache.GetTableName<ProductCategory>();
    private static readonly string ProductMasterTable = EntityMetadataCache.GetTableName<ProductMaster>();

    public async Task<IReadOnlyList<ProductCategory>> GetSubById(Guid parentId)
    {
        // 递归查询包含自身在内的有效分类树。
        var sql = $@"
WITH CategoryTree AS (
    SELECT *
    FROM [{ProductCategoryTable}]
    WHERE Uid = @ParentId AND (IsDeleted = 0 OR IsDeleted IS NULL)

    UNION ALL

    SELECT c.*
    FROM [{ProductCategoryTable}] c
    INNER JOIN CategoryTree ct ON c.ParentId = ct.Uid
    WHERE (c.IsDeleted = 0 OR c.IsDeleted IS NULL)
)
SELECT * FROM CategoryTree;";

        var rows = await Connection.QueryAsync<ProductCategory>(
            sql,
            new { ParentId = parentId },
            transaction: Transaction);
        return [.. rows];
    }

    public async Task<IReadOnlyList<ProductCategory>> GetAllForUpdateAsync()
    {
        var sql = $@"
SELECT
    [Uid], [Version], [IsDeleted], [CreatedDate], [CreatedBy], [UpdatedDate], [UpdatedBy],
    [CategoryName], [ParentId], [FakeName], [CategoryCode], [SortNo]
FROM [{ProductCategoryTable}] WITH (UPDLOCK, ROWLOCK)
WHERE [IsDeleted] = 0 OR [IsDeleted] IS NULL;";

        var rows = await Connection.QueryAsync<ProductCategory>(sql, transaction: Transaction);
        return [.. rows];
    }

    public async Task<IReadOnlyList<Guid>> GetReferencedCategoryIdsAsync(IEnumerable<Guid> categoryIds)
    {
        var normalizedIds = categoryIds?
            .Where(uid => uid != Guid.Empty)
            .Distinct()
            .ToArray() ?? [];
        if (normalizedIds.Length == 0) return [];

        var sql = $@"
SELECT DISTINCT [CategoryId]
FROM [{ProductMasterTable}] WITH (UPDLOCK, ROWLOCK)
WHERE [CategoryId] IN @CategoryIds
  AND ([IsDeleted] = 0 OR [IsDeleted] IS NULL);";

        var ids = await Connection.QueryAsync<Guid>(
            sql,
            new { CategoryIds = normalizedIds },
            transaction: Transaction);
        return [.. ids];
    }

    public async Task<PagedResponseBase<ProductCategory>> GetPagedAsync(ProductCategorySearchRequest request)
    {
        request ??= new ProductCategorySearchRequest();
        var pageIndex = request.PageIndex > 0 ? request.PageIndex : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 20;
        var offset = (pageIndex - 1) * pageSize;

        var parameters = new DynamicParameters();
        parameters.Add("@Offset", offset);
        parameters.Add("@PageSize", pageSize);

        var where = new StringBuilder("WHERE ([IsDeleted] = 0 OR [IsDeleted] IS NULL)");
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("@Keyword", SqlLikePattern.Build(request.Keyword));
            where.Append(@"
  AND (
         [CategoryName] LIKE @Keyword ESCAPE '~'
      OR [FakeName] LIKE @Keyword ESCAPE '~'
      OR [CategoryCode] LIKE @Keyword ESCAPE '~'
  )");
        }

        var sql = $@"
SELECT COUNT(1)
FROM [{ProductCategoryTable}] WITH (NOLOCK)
{where};

SELECT
    [Uid], [Version], [IsDeleted], [CreatedDate], [CreatedBy], [UpdatedDate], [UpdatedBy],
    [CategoryName], [ParentId], [FakeName], [CategoryCode], [SortNo]
FROM [{ProductCategoryTable}] WITH (NOLOCK)
{where}
ORDER BY [SortNo] ASC, [CategoryName] ASC, [Uid] ASC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
        var totalCount = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<ProductCategory>();
        return new PagedResponseBase<ProductCategory>
        {
            Items = [.. items],
            TotalCount = totalCount,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }
}
