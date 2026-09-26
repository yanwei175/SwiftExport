using Dapper;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.MasterData;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.MasterData;

/// <summary>独立采购选择器的只读 SQL；产品始终以主数据为主体，供应商关系仅在明确选择供应商时补充型号和价格。</summary>
public sealed class ProcurementMasterDataQueryRepository(RepositoryBaseContext context) : IProcurementMasterDataQueryRepository
{
    // 与 .NET 空白判断一致，避免仅含制表符或全角空格的编码进入新增候选。
    private const string Whitespace = "\u0009\u000A\u000B\u000C\u000D\u0020\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000";
    private const string CustomerColumns = "m.Uid AS Uid,COALESCE(m.CustomerCode,N'') AS CustomerCode,COALESCE(m.CustomerName,N'') AS CustomerName";
    private const string SupplierColumns = "m.Uid AS Uid,COALESCE(m.SupplierCode,N'') AS SupplierCode,COALESCE(m.SupplierName,N'') AS SupplierName";
    private const string CustomerFilter = """
        FROM dbo.Customer m
        WHERE ISNULL(m.IsDeleted,0)=0 AND m.Uid<>@EmptyUid
          AND (@Keyword IS NULL OR m.CustomerCode LIKE @Keyword ESCAPE N'~' OR m.CustomerName LIKE @Keyword ESCAPE N'~')
        """;
    private const string SupplierFilter = """
        FROM dbo.Supplier m
        WHERE ISNULL(m.IsDeleted,0)=0 AND m.Uid<>@EmptyUid
          AND (@Keyword IS NULL OR m.SupplierCode LIKE @Keyword ESCAPE N'~' OR m.SupplierName LIKE @Keyword ESCAPE N'~')
        """;

    /// <summary>新增客户候选仅包含启用且编码合法的客户；名称未配置时允许空显示，与采购最终校验保持一致。</summary>
    public Task<PagedResponseBase<ProcurementCustomerCandidateDto>> SearchCustomersAsync(
        PagedRequestBase request, CancellationToken cancellationToken)
        => ReadPageAsync<ProcurementCustomerCandidateDto>(CustomerColumns,
            CustomerFilter + " AND m.IsActive=1 AND NULLIF(TRIM(@Whitespace FROM m.CustomerCode),N'') IS NOT NULL",
            "m.CustomerCode,m.Uid", request, cancellationToken);

    /// <summary>新增供应商候选要求启用、编码和名称合法，不要求产品供应商目录关系。</summary>
    public Task<PagedResponseBase<ProcurementSupplierCandidateDto>> SearchSuppliersAsync(
        PagedRequestBase request, CancellationToken cancellationToken)
        => ReadPageAsync<ProcurementSupplierCandidateDto>(SupplierColumns,
            SupplierFilter + " AND m.IsActive=1 AND NULLIF(TRIM(@Whitespace FROM m.SupplierCode),N'') IS NOT NULL AND NULLIF(TRIM(@Whitespace FROM m.SupplierName),N'') IS NOT NULL",
            "m.SupplierCode,m.Uid", request, cancellationToken);

    /// <summary>返回启用且身份完整的采购供货产品；单位和物料类型原样展示，最终资格由创建命令校验。</summary>
    public Task<PagedResponseBase<ProcurementProductCandidateDto>> SearchProductsAsync(
        ProcurementProductSearchRequest request, CancellationToken cancellationToken)
    {
        const string commonColumns = """
            p.Uid AS Uid,p.Sku AS Sku,COALESCE(p.MainImage,N'') AS MainImage,
            COALESCE(cp.CustomerSku,N'') AS CustomerSku,COALESCE(p.LinkSku,N'') AS LinkSku,
            {0} AS SupplierSku,COALESCE(p.DescriptionZh,N'') AS DescriptionZh,
            COALESCE(p.DescriptionEn,N'') AS DescriptionEn,COALESCE(color.ColorNameZh,N'') AS FinishZh,
            COALESCE(p.FeaturesZh,N'') AS FeaturesZh,COALESCE(p.MaterialType,N'') AS MaterialType,
            COALESCE(p.SupplyType,N'') AS SupplyType,COALESCE(p.Unit,N'') AS Unit,{1} AS UnitPrice,{2} AS TaxPrice
            """;
        const string commonFrom = """
            FROM dbo.Product p
            LEFT JOIN dbo.ProductColor color ON color.Uid=p.ColorId AND ISNULL(color.IsDeleted,0)=0
            OUTER APPLY (
                SELECT TOP(1) relation.CustomerSku
                FROM dbo.CustomerProduct relation
                WHERE @CustomerId IS NOT NULL AND relation.CustomerId=@CustomerId AND relation.SkuId=p.Uid
                  AND relation.IsActive=1 AND ISNULL(relation.IsDeleted,0)=0
                ORDER BY relation.Uid
            ) cp
            {0}
            WHERE ISNULL(p.IsDeleted,0)=0 AND p.IsActive=1 AND p.Uid<>@EmptyUid
              AND NULLIF(TRIM(@Whitespace FROM p.Sku),N'') IS NOT NULL
              AND p.SupplyType=@PurchaseSupply
              AND (@Keyword IS NULL OR p.Sku LIKE @Keyword ESCAPE N'~'
                   OR p.DescriptionZh LIKE @Keyword ESCAPE N'~' OR p.DescriptionEn LIKE @Keyword ESCAPE N'~'
                   OR cp.CustomerSku LIKE @Keyword ESCAPE N'~' OR {1} LIKE @Keyword ESCAPE N'~')
            """;

        if (!request.SupplierId.HasValue)
            return ReadPageAsync<ProcurementProductCandidateDto>(
                string.Format(commonColumns, "CAST(N'' AS nvarchar(200))", "CAST(NULL AS decimal(18,6))", "CAST(NULL AS decimal(18,6))"),
                string.Format(commonFrom, string.Empty, "CAST(N'' AS nvarchar(200))"), "p.Sku,p.Uid", request, cancellationToken);

        const string supplierApply = """
            OUTER APPLY (
                SELECT TOP(1) relation.SupplierSku,relation.UnitPrice,relation.TaxPrice
                FROM dbo.SupplierProduct relation
                WHERE relation.SupplierId=@SupplierId AND relation.SkuId=p.Uid AND ISNULL(relation.IsDeleted,0)=0
                ORDER BY relation.Uid
            ) sp
            """;
        return ReadPageAsync<ProcurementProductCandidateDto>(
            string.Format(commonColumns, "COALESCE(sp.SupplierSku,N'')", "sp.UnitPrice", "sp.TaxPrice"),
            string.Format(commonFrom, supplierApply, "sp.SupplierSku"), "p.Sku,p.Uid", request, cancellationToken);
    }

    /// <summary>历史客户筛选保留停用对象，返回当前显示资料；不回写采购快照，也不宣称软删除对象可用于新增。</summary>
    public Task<PagedResponseBase<ProcurementCustomerFilterDto>> SearchCustomerHistoryFiltersAsync(
        PagedRequestBase request, CancellationToken cancellationToken)
        => ReadPageAsync<ProcurementCustomerFilterDto>(CustomerColumns + ",m.IsActive AS IsActive",
            CustomerFilter, "m.CustomerCode,m.Uid", request, cancellationToken);

    /// <summary>历史供应商筛选不套用新增资格；停用和资料不完整不应导致旧订单无法按稳定身份筛选。</summary>
    public Task<PagedResponseBase<ProcurementSupplierFilterDto>> SearchSupplierHistoryFiltersAsync(
        PagedRequestBase request, CancellationToken cancellationToken)
        => ReadPageAsync<ProcurementSupplierFilterDto>(SupplierColumns + ",m.IsActive AS IsActive",
            SupplierFilter, "m.SupplierCode,m.Uid", request, cancellationToken);

    /// <summary>只拼接本类固定 SQL 片段；关键词按字面包含匹配转义，计数与稳定分页共用过滤条件和事务。</summary>
    private async Task<PagedResponseBase<T>> ReadPageAsync<T>(string columns, string filter, string order,
        PagedRequestBase request, CancellationToken cancellationToken)
    {
        var keyword = string.IsNullOrEmpty(request.Keyword) ? null
            : "%" + request.Keyword.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
        using var grid = await context.Session.Connection.QueryMultipleAsync(Command(
            "SELECT COUNT(*) " + filter + "; SELECT " + columns + " " + filter
            + " ORDER BY " + order + " OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;",
            new
            {
                Keyword = keyword, Whitespace, EmptyUid = Guid.Empty,
                CustomerId = (request as ProcurementProductSearchRequest)?.CustomerId,
                SupplierId = (request as ProcurementProductSearchRequest)?.SupplierId,
                Skip = (long)(request.PageIndex - 1) * request.PageSize, Take = request.PageSize,
                PurchaseSupply = SupplyTypeEnums.采购
            }, cancellationToken));
        var count = await grid.ReadSingleAsync<int>();
        var items = (await grid.ReadAsync<T>()).ToList();
        return new PagedResponseBase<T>
        {
            Items = items, TotalCount = count, PageIndex = request.PageIndex, PageSize = request.PageSize
        };
    }

    /// <summary>强制所有读取加入服务拥有的事务，透传取消令牌，避免授权与分页使用不同读取范围。</summary>
    private CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken)
        => new(sql, parameters, context.Session.Transaction ?? throw new InvalidOperationException("候选查询需要只读事务。"),
            cancellationToken: cancellationToken);
}
