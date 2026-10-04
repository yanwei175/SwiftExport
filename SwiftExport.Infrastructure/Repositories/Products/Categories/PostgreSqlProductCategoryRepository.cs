using Dapper;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.Categories;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.Categories;

/// <summary>产品分类 PostgreSQL 仓储；查询和维护复用调用方会话，锁仅作用于既有分类及被引用主型号行。</summary>
public sealed class PostgreSqlProductCategoryRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<ProductCategory>(context), IProductCategoryRepository
{
    private static readonly string EntityColumns = string.Join(",", Metadata.Columns.Select(column =>
        $"c.{PostgreSqlNames.Quote(column.ColumnName)} AS {PostgreSqlNames.Quote(column.PropertyName)}"));
    private static readonly string NarrowColumns = string.Join(",", new[]
    {
        nameof(ProductCategory.Uid), nameof(ProductCategory.Version), nameof(ProductCategory.IsDeleted),
        nameof(ProductCategory.CreatedDate), nameof(ProductCategory.CreatedBy), nameof(ProductCategory.UpdatedDate),
        nameof(ProductCategory.UpdatedBy), nameof(ProductCategory.CategoryName), nameof(ProductCategory.ParentId),
        nameof(ProductCategory.FakeName), nameof(ProductCategory.CategoryCode), nameof(ProductCategory.SortNo)
    }.Select(property => $"c.{PostgreSqlNames.Quote(Metadata.GetProperty(property).ColumnName)} AS {PostgreSqlNames.Quote(property)}"));

    /// <summary>读取包含自身的有效子树；深度一百仍可返回，超界或循环固定失败，不返回截断树。</summary>
    public async Task<IReadOnlyList<ProductCategory>> GetSubById(Guid parentId)
    {
        var physicalColumns = string.Join(",", Metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
        var childColumns = string.Join(",", Metadata.Columns.Select(column => "c." + PostgreSqlNames.Quote(column.ColumnName)));
        // 多生成失败边界的一层才能识别超限；限定递归深度也使坏数据循环有限结束。
        var rows = (await Connection.QueryAsync<ProductCategory, int, (ProductCategory Entity, int Depth)>($"""
            WITH RECURSIVE category_tree ({physicalColumns},depth) AS (
                SELECT {childColumns},0 FROM public.product_category c
                WHERE c.uid=@ParentId AND COALESCE(c.is_deleted,FALSE)=FALSE
                UNION ALL
                SELECT {childColumns},ct.depth+1 FROM public.product_category c
                JOIN category_tree ct ON c.parent_id=ct.uid
                WHERE COALESCE(c.is_deleted,FALSE)=FALSE AND ct.depth<101
            )
            SELECT {EntityColumns},c.depth AS "Depth" FROM category_tree c
            """, (entity, depth) => (entity, depth), new { ParentId = parentId }, Transaction, splitOn: "Depth")).ToArray();
        if (rows.Any(row => row.Depth > 100))
            throw new InvalidOperationException("产品分类树超过允许的递归深度一百，可能存在循环引用。");
        return [.. rows.Select(row => row.Entity)];
    }

    /// <summary>批量维护锁住现存未软删分类，按身份顺序取行锁；不创建事务或保护不存在的行。</summary>
    public async Task<IReadOnlyList<ProductCategory>> GetAllForUpdateAsync()
    {
        RequireTransaction();
        var rows = await Connection.QueryAsync<ProductCategory>(
            $"SELECT {NarrowColumns} FROM public.product_category c WHERE COALESCE(c.is_deleted,FALSE)=FALSE ORDER BY c.uid FOR UPDATE",
            transaction: Transaction);
        return [.. rows];
    }

    /// <summary>只锁指定分类下的有效主型号底层行，读取后去重分类身份；空输入不访问数据库或要求事务。</summary>
    public async Task<IReadOnlyList<Guid>> GetReferencedCategoryIdsAsync(IEnumerable<Guid> categoryIds)
    {
        var ids = categoryIds?.Where(uid => uid != Guid.Empty).Distinct().ToArray() ?? [];
        if (ids.Length == 0) return [];
        RequireTransaction();
        var rows = await Connection.QueryAsync<Guid>("""
            SELECT pm.category_id FROM public.product_master pm
            WHERE pm.category_id=ANY(@Uids) AND COALESCE(pm.is_deleted,FALSE)=FALSE
            ORDER BY pm.uid FOR UPDATE
            """, PostgreSqlParameters.Ids(ids), Transaction);
        return [.. rows.Distinct()];
    }

    /// <summary>保留未删分类窄投影、原通配规则和默认每页二十条，无新增上限；普通分页不取行锁。</summary>
    public async Task<PagedResponseBase<ProductCategory>> GetPagedAsync(ProductCategorySearchRequest request)
    {
        request ??= new ProductCategorySearchRequest();
        var pageIndex = request.PageIndex > 0 ? request.PageIndex : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 20;
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        var where = "WHERE COALESCE(c.is_deleted,FALSE)=FALSE";
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            where += """
                 AND (c.category_name COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR c.fake_name COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR c.category_code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')
                """;
        }
        var count = await Connection.ExecuteScalarAsync<int>(
            $"SELECT count(*) FROM public.product_category c {where}", parameters, Transaction);
        var rows = await Connection.QueryAsync<ProductCategory>($"""
            SELECT {NarrowColumns} FROM public.product_category c {where}
            ORDER BY c.sort_no ASC NULLS FIRST,c.category_name COLLATE public.erp_dictionary_ci ASC NULLS FIRST,c.uid ASC
            LIMIT @PageSize OFFSET @Offset
            """, parameters, Transaction);
        return new PagedResponseBase<ProductCategory>
        { Items = [.. rows], TotalCount = count, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>维护锁必须随调用方事务持续，禁止隐式自动提交让锁查询形同普通读取。</summary>
    private void RequireTransaction()
    {
        if (Transaction is null) throw new InvalidOperationException("产品分类维护锁查询需要调用方事务。");
    }
}
