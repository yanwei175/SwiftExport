using Dapper;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.MasterData;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.MasterData;

/// <summary>在调用方只读事务内查询 PostgreSQL 采购候选；主数据资格与关系补充沿用源查询边界。</summary>
public sealed class PostgreSqlProcurementMasterDataQueryRepository(RepositoryBaseContext context) : IProcurementMasterDataQueryRepository
{
    // 与源查询相同的 .NET Unicode 空白集合，不将数据库默认 ASCII 修剪当作业务合法性判断。
    private const string Whitespace = "\u0009\u000A\u000B\u000C\u000D\u0020\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000";
    private const string CustomerColumns = "m.uid AS \"Uid\",COALESCE(m.customer_code,'') AS \"CustomerCode\",COALESCE(m.customer_name,'') AS \"CustomerName\"";
    private const string SupplierColumns = "m.uid AS \"Uid\",COALESCE(m.supplier_code,'') AS \"SupplierCode\",COALESCE(m.supplier_name,'') AS \"SupplierName\"";
    private const string CustomerFilter = """
        FROM public.customer m
        WHERE COALESCE(m.is_deleted,FALSE)=FALSE AND m.uid<>@EmptyUid
          AND (CAST(@Keyword AS text) IS NULL OR m.customer_code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
               OR m.customer_name COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')
        """;
    private const string SupplierFilter = """
        FROM public.supplier m
        WHERE COALESCE(m.is_deleted,FALSE)=FALSE AND m.uid<>@EmptyUid
          AND (CAST(@Keyword AS text) IS NULL OR m.supplier_code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
               OR m.supplier_name COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')
        """;

    /// <summary>新增客户候选须启用、身份非空且编码不是 Unicode 空白；名称允许空显示。</summary>
    public Task<PagedResponseBase<ProcurementCustomerCandidateDto>> SearchCustomersAsync(PagedRequestBase request, CancellationToken cancellationToken)
        => ReadPageAsync<ProcurementCustomerCandidateDto>(CustomerColumns,
            CustomerFilter + " AND m.is_active=TRUE AND NULLIF(btrim(m.customer_code,@Whitespace),'') IS NOT NULL",
            "m.customer_code COLLATE public.erp_dictionary_ci,m.uid", request, cancellationToken);

    /// <summary>新增供应商候选须启用且编码、名称均完整；不要求供应商产品关系。</summary>
    public Task<PagedResponseBase<ProcurementSupplierCandidateDto>> SearchSuppliersAsync(PagedRequestBase request, CancellationToken cancellationToken)
        => ReadPageAsync<ProcurementSupplierCandidateDto>(SupplierColumns,
            SupplierFilter + " AND m.is_active=TRUE AND NULLIF(btrim(m.supplier_code,@Whitespace),'') IS NOT NULL AND NULLIF(btrim(m.supplier_name,@Whitespace),'') IS NOT NULL",
            "m.supplier_code COLLATE public.erp_dictionary_ci,m.uid", request, cancellationToken);

    /// <summary>采购产品为查询主体；客户关系须启用，供应商关系仅排软删，缺失关系不排除产品或代用默认供应商。</summary>
    public Task<PagedResponseBase<ProcurementProductCandidateDto>> SearchProductsAsync(ProcurementProductSearchRequest request, CancellationToken cancellationToken)
    {
        var hasSupplier = request.SupplierId.HasValue;
        var supplierSku = hasSupplier ? "COALESCE(sp.supplier_sku,'')" : "CAST('' AS text)";
        var unitPrice = hasSupplier ? "sp.unit_price" : "CAST(NULL AS numeric(18,6))";
        var taxPrice = hasSupplier ? "sp.tax_price" : "CAST(NULL AS numeric(18,6))";
        var columns = $"""
            p.uid AS "Uid",p.sku AS "Sku",COALESCE(p.main_image,'') AS "MainImage",
            COALESCE(cp.customer_sku,'') AS "CustomerSku",COALESCE(pn.product_code,'') AS "LinkSku",
            {supplierSku} AS "SupplierSku",COALESCE(p.description_zh,'') AS "DescriptionZh",
            COALESCE(p.description_en,'') AS "DescriptionEn",COALESCE(color.color_name_zh,'') AS "FinishZh",
            COALESCE(p.features_zh,'') AS "FeaturesZh",COALESCE(p.material_type,'') AS "MaterialType",
            COALESCE(p.supply_type,'') AS "SupplyType",COALESCE(p.unit,'') AS "Unit",{unitPrice} AS "UnitPrice",{taxPrice} AS "TaxPrice"
            """;
        var supplierJoin = hasSupplier ? """
            LEFT JOIN LATERAL (
                SELECT relation.supplier_sku,relation.unit_price,relation.tax_price
                FROM public.supplier_product relation
                WHERE relation.supplier_id=CAST(@SupplierId AS uuid) AND relation.sku_id=p.uid AND COALESCE(relation.is_deleted,FALSE)=FALSE
                ORDER BY relation.uid LIMIT 1
            ) sp ON TRUE
            """ : string.Empty;
        var filter = $"""
            FROM public.product p
            LEFT JOIN public.product_neutral pn ON pn.uid=p.product_neutral_id AND pn.is_deleted=FALSE
            LEFT JOIN public.product_color color ON color.uid=p.color_id AND COALESCE(color.is_deleted,FALSE)=FALSE
            LEFT JOIN LATERAL (
                SELECT relation.customer_sku FROM public.customer_product relation
                WHERE CAST(@CustomerId AS uuid) IS NOT NULL AND relation.customer_id=CAST(@CustomerId AS uuid) AND relation.sku_id=p.uid
                  AND relation.is_active=TRUE AND COALESCE(relation.is_deleted,FALSE)=FALSE
                ORDER BY relation.uid LIMIT 1
            ) cp ON TRUE
            {supplierJoin}
            WHERE COALESCE(p.is_deleted,FALSE)=FALSE AND p.is_active=TRUE AND p.uid<>@EmptyUid
              AND NULLIF(btrim(p.sku,@Whitespace),'') IS NOT NULL
              AND p.supply_type COLLATE public.erp_dictionary_ci = @PurchaseSupply
              AND (CAST(@Keyword AS text) IS NULL OR p.sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                   OR p.description_zh COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                   OR p.description_en COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                   OR cp.customer_sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                   OR {supplierSku} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')
            """;
        return ReadPageAsync<ProcurementProductCandidateDto>(columns, filter,
            "p.sku COLLATE public.erp_dictionary_ci,p.uid", request, cancellationToken);
    }

    /// <summary>历史客户筛选保留停用及资料不完整对象，只排软删和空身份，不回写历史快照。</summary>
    public Task<PagedResponseBase<ProcurementCustomerFilterDto>> SearchCustomerHistoryFiltersAsync(PagedRequestBase request, CancellationToken cancellationToken)
        => ReadPageAsync<ProcurementCustomerFilterDto>(CustomerColumns + ",m.is_active AS \"IsActive\"", CustomerFilter,
            "m.customer_code COLLATE public.erp_dictionary_ci,m.uid", request, cancellationToken);

    /// <summary>历史供应商筛选保留停用和空白编码名称，不套用新增候选资格。</summary>
    public Task<PagedResponseBase<ProcurementSupplierFilterDto>> SearchSupplierHistoryFiltersAsync(PagedRequestBase request, CancellationToken cancellationToken)
        => ReadPageAsync<ProcurementSupplierFilterDto>(SupplierColumns + ",m.is_active AS \"IsActive\"", SupplierFilter,
            "m.supplier_code COLLATE public.erp_dictionary_ci,m.uid", request, cancellationToken);

    /// <summary>计数和页面分两命令共用调用方事务及取消令牌；只转义 PG LIKE 特殊字符，不接管服务的分页校验或事务。</summary>
    private async Task<PagedResponseBase<T>> ReadPageAsync<T>(string columns, string filter, string order,
        PagedRequestBase request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = context.Session.Transaction ?? throw new InvalidOperationException("候选查询需要只读事务。");
        var keyword = string.IsNullOrEmpty(request.Keyword) ? null
            : "%" + request.Keyword.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_") + "%";
        var parameters = new
        {
            Keyword = keyword, Whitespace, EmptyUid = Guid.Empty,
            CustomerId = (request as ProcurementProductSearchRequest)?.CustomerId,
            SupplierId = (request as ProcurementProductSearchRequest)?.SupplierId,
            Skip = (long)(request.PageIndex - 1) * request.PageSize, Take = request.PageSize,
            PurchaseSupply = SupplyTypeEnums.采购
        };
        var count = await context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) " + filter, parameters, transaction, cancellationToken: cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        var items = (await context.Session.Connection.QueryAsync<T>(new CommandDefinition(
            "SELECT " + columns + " " + filter + " ORDER BY " + order + " LIMIT @Take OFFSET @Skip",
            parameters, transaction, cancellationToken: cancellationToken))).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        return new PagedResponseBase<T> { Items = items, TotalCount = count, PageIndex = request.PageIndex, PageSize = request.PageSize };
    }
}
