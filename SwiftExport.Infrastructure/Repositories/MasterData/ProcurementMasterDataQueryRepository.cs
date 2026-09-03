using Dapper;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.MasterData;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.MasterData;

/// <summary>独立采购选择器的只读 SQL；身份来自主数据 Uid，不读取联系方式、价格、默认供应商或产品目录关系。</summary>
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

    /// <summary>重验真实账号、角色和关联；锁保持至查询事务结束，不信任客户端角色或仅登录标志。</summary>
    public async Task<bool> HasPermissionAsync(Guid userId, string requiredRole, CancellationToken cancellationToken)
        => await context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CAST(CASE WHEN EXISTS(
                SELECT 1 FROM dbo.SysUser u WITH(HOLDLOCK)
                JOIN dbo.SysUser_Roles ur WITH(HOLDLOCK) ON ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0
                JOIN dbo.SysRoles r WITH(HOLDLOCK) ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0
                WHERE u.Uid=@UserId AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
                  AND r.RoleName IN(N'Admin',@RequiredRole)
            ) THEN 1 ELSE 0 END AS bit);
            """, new { UserId = userId, RequiredRole = requiredRole }, cancellationToken));

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

    /// <summary>仅返回启用的标准采购成品、零件或客供件；单位必须命中唯一启用字典项，重复字典不放大分页行数。</summary>
    public Task<PagedResponseBase<ProcurementProductCandidateDto>> SearchProductsAsync(
        PagedRequestBase request, CancellationToken cancellationToken)
        => ReadPageAsync<ProcurementProductCandidateDto>("""
            p.Uid AS Uid,p.Sku AS Sku,COALESCE(p.DescriptionZh,N'') AS DescriptionZh,
            COALESCE(p.DescriptionEn,N'') AS DescriptionEn,u.Unit AS Unit
            """, """
            FROM dbo.Product p
            CROSS APPLY (
                SELECT MIN(LTRIM(RTRIM(d.DataNameEn))) AS Unit
                FROM dbo.SysDictData d
                WHERE d.DictCode=N'Unit' AND d.IsEnabled=1 AND ISNULL(d.IsDeleted,0)=0
                  AND UPPER(LTRIM(RTRIM(d.DataNameEn)))=UPPER(TRIM(@Whitespace FROM p.Unit))
                HAVING COUNT(*)=1 AND NULLIF(TRIM(@Whitespace FROM MIN(LTRIM(RTRIM(d.DataNameEn)))),N'') IS NOT NULL
            ) u
            WHERE ISNULL(p.IsDeleted,0)=0 AND p.IsActive=1 AND p.Uid<>@EmptyUid
              AND NULLIF(TRIM(@Whitespace FROM p.Sku),N'') IS NOT NULL
              AND LEFT(p.Sku,LEN(@NonStandardPrefix))<>@NonStandardPrefix
              AND p.SupplyType=@PurchaseSupply AND p.MaterialType IN @AllowedMaterialTypes
              AND (@Keyword IS NULL OR p.Sku LIKE @Keyword ESCAPE N'~'
                   OR p.DescriptionZh LIKE @Keyword ESCAPE N'~' OR p.DescriptionEn LIKE @Keyword ESCAPE N'~')
            """, "p.Sku,p.Uid", request, cancellationToken);

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
                Skip = (long)(request.PageIndex - 1) * request.PageSize, Take = request.PageSize,
                NonStandardPrefix = DocumentTypeStaticsEnum.NonStandardPart, PurchaseSupply = SupplyTypeEnums.采购,
                AllowedMaterialTypes = new[] { MaterialTypesEnum.成品, MaterialTypesEnum.零件, MaterialTypesEnum.客供件 }
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
