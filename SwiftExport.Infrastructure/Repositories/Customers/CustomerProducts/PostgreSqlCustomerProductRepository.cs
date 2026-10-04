using Dapper;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces.Customers.CustomerProducts;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Customers.CustomerProducts;

/// <summary>客户产品 PostgreSQL 仓储，保留可选产品关联及当前客户展示；读写使用调用方会话，不新增资格锁或唯一错误分类。</summary>
public sealed class PostgreSqlCustomerProductRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<CustomerProduct>(context), ICustomerProductRepository
{
    private static readonly string EntityColumns = string.Join(",", Metadata.Columns.Select(column =>
        $"cp.{PostgreSqlNames.Quote(column.ColumnName)} AS {PostgreSqlNames.Quote(column.PropertyName)}"));
    private static readonly string RelationColumns = string.Join(",", new[]
    {
        "Uid", "Version", "IsDeleted", "CreatedDate", "CreatedBy", "UpdatedDate", "UpdatedBy", "CustomerId", "SkuId", "Sku", "LinkSku",
        "DescriptionEn", "FeaturesEn", "CustomerSku", "CustomerPdDescription", "CustomerPdSeries", "CustomerColorName", "CustomerPdFeatures",
        "CustomerBarcode1", "CustomerBarcode2", "USDPrice", "CNYPrice", "LeadTimeDays", "MOQ", "IsActive", "Note"
    }.Select(property => $"cp.{PostgreSqlNames.Quote(Metadata.GetProperty(property).ColumnName)} AS {PostgreSqlNames.Quote(property)}"));
    private const string DisplayColumns = """
        c.customer_code AS "CustomerCode",COALESCE(p.description_en,cp.description_en) AS "Description",
        CASE WHEN pm.uid IS NULL OR pm.category_id IS NULL OR pm.category_id=@EmptyGuid THEN '未分类'
             ELSE COALESCE(pc.category_name,'分类已失效') END AS "CategoryName",
        p.material_type AS "MaterialType",p.main_image AS "MainImage"
        """;
    private const string Joins = """
        FROM public.customer_product cp
        INNER JOIN public.customer c ON c.uid=cp.customer_id
        LEFT JOIN public.product p ON p.uid=cp.sku_id AND (p.is_deleted=FALSE OR p.is_deleted IS NULL)
        LEFT JOIN public.product_master pm ON pm.uid=p.product_master_id AND (pm.is_deleted=FALSE OR pm.is_deleted IS NULL)
        LEFT JOIN public.product_category pc ON pc.uid=pm.category_id AND (pc.is_deleted=FALSE OR pc.is_deleted IS NULL)
        """;
    private const string VisibleRelation = "(cp.is_deleted=FALSE OR cp.is_deleted IS NULL)";
    private const string VisibleDisplay = "WHERE (cp.is_deleted=FALSE OR cp.is_deleted IS NULL) AND (c.is_deleted=FALSE OR c.is_deleted IS NULL)";

    /// <summary>读取关系详情，客户已删除或缺失时不展示；产品关联为空、缺失或删除仍保留关系及其快照。</summary>
    public Task<CustomerProductDto?> GetDtoByIdAsync(Guid uid) => Connection.QueryFirstOrDefaultAsync<CustomerProductDto>($"""
        SELECT {RelationColumns},{DisplayColumns} {Joins} {VisibleDisplay} AND cp.uid=@Uid
        """, new { Uid = uid, EmptyGuid = Guid.Empty }, Transaction);

    /// <summary>仅按原客户单选和普通关键词分页；其他请求占位字段不擅自生效，停用行保留。</summary>
    public async Task<PagedResponseBase<CustomerProductDto>> GetPagedAsync(CustomerProductSearchRequest request)
    {
        request ??= new CustomerProductSearchRequest();
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Min(request.PageSize <= 0 ? 20 : request.PageSize, 200);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        parameters.Add("EmptyGuid", Guid.Empty);
        var where = VisibleDisplay;
        if (request.CustomerId != Guid.Empty)
        {
            parameters.Add("CustomerId", request.CustomerId);
            where += " AND cp.customer_id=@CustomerId";
        }
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            where += " AND (" + string.Join(" OR ", new[]
                { "cp.customer_sku", "cp.sku", "cp.link_sku", "cp.customer_pd_description", "cp.customer_pd_series" }
                .Select(column => $"{column} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'")) + ")";
        }
        // 计数与取页复用参数和调用方事务，多对一关联不会放大客户产品数量。
        var total = await Connection.ExecuteScalarAsync<int>($"SELECT count(*) {Joins} {where}", parameters, Transaction);
        var rows = await Connection.QueryAsync<CustomerProductDto>($"""
            SELECT {RelationColumns},{DisplayColumns} {Joins} {where}
            ORDER BY cp.customer_sku COLLATE public.erp_dictionary_ci ASC NULLS FIRST,cp.uid LIMIT @PageSize OFFSET @Offset
            """, parameters, Transaction);
        return new PagedResponseBase<CustomerProductDto>
        { Items = rows.ToList(), TotalCount = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>返回未删除关系上的客户身份，包含停用及悬空客户；当前客户是否存在由原调用方后续查询判断。</summary>
    public async Task<IReadOnlyList<Guid>> GetAllCustomerIdListAsync() => [.. await Connection.QueryAsync<Guid>($"""
        SELECT DISTINCT cp.customer_id AS "CustomerId" FROM public.customer_product cp
        WHERE cp.customer_id IS NOT NULL AND {VisibleRelation}
        """, transaction: Transaction)];

    /// <summary>以原始客户料号和客户身份查实体，保留关系启停资格与可选产品关联，不修剪调用方输入。</summary>
    public Task<CustomerProduct?> GetByCustomerSkuAsync(string customerSku, Guid customerId) =>
        Connection.QueryFirstOrDefaultAsync<CustomerProduct>($"""
            SELECT {EntityColumns} FROM public.customer_product cp
            WHERE {EqualCode("cp.customer_sku", "@CustomerSku")} AND cp.customer_id=@CustomerId AND {VisibleRelation}
            LIMIT 1
            """, new { CustomerSku = customerSku, CustomerId = customerId }, Transaction);

    /// <summary>按同客户未删除关系检查原始料号集合，不过滤空白或去重输入；空或空值集合返回无匹配。</summary>
    public async Task<IReadOnlyList<string>> GetExistingCustomerSkuListAsync(IEnumerable<string> skuList, Guid customerId)
    {
        var values = skuList?.ToArray() ?? [];
        if (values.Length == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("SkuList", values);
        parameters.Add("CustomerId", customerId);
        return [.. await Connection.QueryAsync<string>($"""
            SELECT cp.customer_sku AS "CustomerSku" FROM public.customer_product cp
            WHERE cp.customer_id=@CustomerId AND {CodeSet("SkuList")} AND {VisibleRelation}
            """, parameters, Transaction)];
    }

    /// <summary>查同客户其他记录占用的料号，排除提交集合全部身份而非逐项配对；按原等价规则去重并返回原存储文本。</summary>
    public async Task<IReadOnlyList<string>> GetCustomerSkusUsedByOtherRecordsAsync(Guid customerId, IEnumerable<(Guid Uid, string CustomerSku)> items)
    {
        var materialized = items.ToArray();
        if (materialized.Length == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.Add("CustomerId", customerId);
        parameters.AddTexts("Codes", materialized.Select(item => item.CustomerSku));
        parameters.AddIds("Uids", materialized.Select(item => item.Uid));
        // DISTINCT ON 只用于原 DISTINCT 方法；原文保留，Uid 仅决定等价组的代表行。
        return [.. await Connection.QueryAsync<string>($"""
            SELECT DISTINCT ON (rtrim(cp.customer_sku,' ') COLLATE public.erp_dictionary_ci) cp.customer_sku AS "CustomerSku"
            FROM public.customer_product cp
            WHERE cp.customer_id=@CustomerId AND {CodeSet("Codes")} AND NOT (cp.uid=ANY(@Uids)) AND {VisibleRelation}
            ORDER BY rtrim(cp.customer_sku,' ') COLLATE public.erp_dictionary_ci ASC NULLS FIRST,cp.uid
            """, parameters, Transaction)];
    }

    /// <summary>返回指定客户集合中仍有未删除关系的身份，仅按原规则去重，不过滤空 Guid 或客户启停。</summary>
    public async Task<IReadOnlyList<Guid>> GetCustomerIdsWithProductsAsync(IEnumerable<Guid> customerIds)
    {
        var ids = customerIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var parameters = PostgreSqlParameters.Ids(ids);
        return [.. await Connection.QueryAsync<Guid>($"""
            SELECT DISTINCT cp.customer_id AS "CustomerId" FROM public.customer_product cp
            WHERE cp.customer_id=ANY(@Uids) AND {VisibleRelation}
            """, parameters, Transaction)];
    }

    /// <summary>编码等值仅规范尾部 ASCII 空格并显式指定 ICU，前导空白和原存储值不改变。</summary>
    private static string EqualCode(string column, string value) =>
        $"rtrim({column},' ') COLLATE public.erp_dictionary_ci = rtrim({value},' ') COLLATE public.erp_dictionary_ci";

    /// <summary>文本数组用 EXISTS 避免重复输入放大行数，不把文本与身份构造成未授权配对规则。</summary>
    private static string CodeSet(string parameter) =>
        $"EXISTS (SELECT 1 FROM unnest(@{parameter}) input(code) WHERE {EqualCode("cp.customer_sku", "input.code")})";
}
