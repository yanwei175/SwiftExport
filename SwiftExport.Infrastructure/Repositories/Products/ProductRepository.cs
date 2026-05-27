using Dapper;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData; // 引用 Core
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Infrastructure.Repositories.Products;



public partial class ProductRepository(RepositoryBaseContext context) : DapperRepository<Product>(context), IProductRepository
{

    public async Task<IReadOnlyList<string>> GetAllCustomerCodeListAsync()
    {
        var sql = $"SELECT CustomerCode FROM [{TableName}] WHERE CustomerCode <> '' AND IsDeleted = 0 GROUP BY CustomerCode";
        var rst = await Connection.QueryAsync<string>(sql, transaction: Transaction);
        return [.. rst];
    }

    public async Task<Product?> GetBySkuAsync(string sku)
    {
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE [Sku] = @Sku AND IsDeleted = 0";
        return await Connection.QueryFirstOrDefaultAsync<Product>(sql, new { Sku = sku }, transaction: Transaction);
    }

    /// <summary>
    /// 按 SKU 列表批量查询产品。
    /// </summary>
    public async Task<IReadOnlyList<Product>> GetBySkuListAsync(IEnumerable<string> skuList)
    {
        if (skuList == null) return [];

        var normalized = skuList
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0) return [];

        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE [Sku] IN @SkuList AND (IsDeleted = 0 OR IsDeleted IS NULL)";
        var rst = await Connection.QueryAsync<Product>(sql, new { SkuList = normalized }, transaction: Transaction);
        return [.. rst];
    }

    public async Task<PagedResult<ProductDto>> GetPagedProductDtoAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Id DESC")
    {
        // 1. 规范化排序字段（如果外部传的是 Id，变为 t.Id 防止多表冲突）
        if (string.IsNullOrWhiteSpace(orderBy))
            orderBy = "t.Id DESC";
        else
            orderBy = $"t.{OrderByRegex().Replace(orderBy, "")}";

        if (string.IsNullOrWhiteSpace(orderBy))
            orderBy = "Id DESC";

        var sb = new System.Text.StringBuilder("WHERE (t.IsDeleted = 0 OR t.IsDeleted IS NULL)");
        var parameters = new DynamicParameters();

        // 3. 调用解析器，传入 "t." 前缀！
        // 这样无论 criteria 里的字段是 Main表字段还是 Join表字段，都会指向派生表 t 的列
        var whereClause = ParseQueryCriteria(criteria, parameters, "t.");
        if (!string.IsNullOrWhiteSpace(whereClause))
            sb.Append(whereClause);

        if (pageIndex < 1) pageIndex = 1;
        parameters.Add("@Offset", (pageIndex - 1) * pageSize);
        parameters.Add("@Limit", pageSize);

        string baseQuery = $@"
                            SELECT 
                                p.*, 
                                pat.CategoryName, 
                                pat.FakeName, 
                                pc.ColorCodeUnique, 
                                pc.ColorCode, 
                                pc.ColorNameEn, 
                                pc.ColorNameZh as Finish,
                                -- 下面这些字段从 APPLY 的别名 pkg 中获取
                                pkg.PcsOfCarton,
                                pkg.NwOfCarton,
                                pkg.GwOfCarton,
                                pkg.CbmOfCarton
                            FROM [{TableName}] p WITH(NOLOCK)
                            LEFT JOIN [ProductColor] pc WITH(NOLOCK) ON p.ColorId = pc.Id AND pc.IsDeleted = 0
                            LEFT JOIN [ProductCategory] pat WITH(NOLOCK) ON p.CategoryId = pat.Id AND pat.IsDeleted = 0
    
                            -- 使用 OUTER APPLY 代替 LEFT JOIN
                            OUTER APPLY (
                                SELECT TOP 1 
                                    pp.PcsPerPackage as PcsOfCarton,
                                    pp.Nw_KG as NwOfCarton,
                                    pp.Gw_KG as GwOfCarton,
                                    pp.Cbm as CbmOfCarton
                                FROM [ProductPackage] pp 
                                WHERE pp.ProductId = p.Id 
                                  AND pp.IsDeleted = 0
                                -- 核心逻辑：优先取默认的，如果没有默认的，取装数最多的（通常是外箱）
                                ORDER BY pp.IsDefault DESC, pp.PcsPerPackage DESC 
                            ) pkg
                        ";

        // 5. 组合最终 SQL
        // 使用派生表 t 包装后，外部的 sb (WHERE t.xxx) 就能完美执行
        string derivedTable = $"({baseQuery}) t";

        var sql = $@"
                    SELECT COUNT(1) FROM {derivedTable} {sb};
                    SELECT * FROM {derivedTable} {sb} 
                    ORDER BY {orderBy} 
                    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

        using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
        var totalCount = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<ProductDto>();

        return new PagedResult<ProductDto>
        {
            TotalCount = totalCount,
            PageIndex = pageIndex,
            PageSize = pageSize,
            Items = [.. items]
        };
    }

    public async Task<IReadOnlyList<string>> GetExistingSkuListAsync(IEnumerable<string> skuList)
    {
        var sql = $"SELECT Sku FROM [{TableName}] WHERE  Sku IN @SkuList";
        var rst = await Connection.QueryAsync<string>(sql, new { SkuList = skuList }, transaction: Transaction);
        return [.. rst];
    }




}