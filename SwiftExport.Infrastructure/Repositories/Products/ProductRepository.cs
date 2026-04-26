using Dapper;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData; // 引用 Core
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.ReadModels.Products;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products;



public partial class ProductRepository(RepositoryBaseContext context) : DapperRepository<Product>(context), IProductRepository
{
    // ⚡ 关键：SQL 映射桥接
    // 数据库里的字段 (Chinese/Pinyin) -> 实体属性 (English)
    private const string SelectSql = @"
        SELECT 
            ID as Id,
            SKU as Sku,
            DescriptionEn as Description,
            
            -- 处理拼音/旧命名映射
            RMBPrice as RmbPrice,
            USDPrice as UsdPrice,
            
            -- 处理那些必填字段 (= default!)
            -- 如果数据库是 NULL，用 ISNULL 给个默认值防止报错
            ISNULL(CustomerCode, '') as CustomerCode,
            ISNULL(CSKU, '') as CSku,
            ISNULL(CDescription, '') as CDescription, 
            
            CreateDate as CreatedTime
        FROM Product 
        WHERE IsDelete = 0"; // 假设你的软删除字段叫 IsDelete

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

    [System.Text.RegularExpressions.GeneratedRegex(@"[^a-zA-Z0-9_,\s\.\[\]]")]
    private static partial System.Text.RegularExpressions.Regex OrderByRegex();

    public override async Task<PagedResult<Product>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Id DESC")
    {
        var readModelPage = await GetPagedReadModelAsync(criteria, pageIndex, pageSize, orderBy);
        return new PagedResult<Product>
        {
            TotalCount = readModelPage.TotalCount,
            PageIndex = readModelPage.PageIndex,
            PageSize = readModelPage.PageSize,
            Items = [.. readModelPage.Items.Select(x => new Product
            {
                Id = x.Id,
                MasterSku = x.MasterSku,
                Sku = x.Sku,
                DescriptionEn = x.DescriptionEn,
                ColorId = x.ColorId,
                FeaturesEn = x.FeaturesEn,
                SupplyType = x.SupplyType ?? string.Empty,
                BomNo = x.BomNo,
                Note = x.Note,
                LinkSku = x.LinkSku,
                MainImage = x.MainImage,
                CustomerCode = x.CustomerCode,
                CSku = x.CSku,
                SupplierCode = x.SupplierCode,
                SSku = x.SSku,
                CategoryId = x.CategoryId
            })]
        };
    }

    public async Task<PagedResult<ProductPagedItem>> GetPagedReadModelAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Id DESC")
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
            pc.ColorNameZh as Finish
        FROM [{TableName}] p WITH(NOLOCK)
        LEFT JOIN [ProductColor] pc WITH(NOLOCK) ON p.ColorId = pc.Id AND (pc.IsDeleted = 0 OR pc.IsDeleted IS NULL)
        LEFT JOIN [ProductCategory] pat WITH(NOLOCK) ON p.CategoryId = pat.Id AND (pat.IsDeleted = 0 OR pat.IsDeleted IS NULL)
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
        var items = await multi.ReadAsync<ProductPagedItem>();

        return new PagedResult<ProductPagedItem>
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