using Dapper;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;

/// <summary>在共享 PostgreSQL 会话中只读查询仓库及供应商候选，保留各用例活动状态与快照回退边界。</summary>
public sealed class PostgreSqlWarehouseQueryRepository(RepositoryBaseContext context) : IWarehouseQueryRepository
{
    private const string SupplierJoin = "LEFT JOIN public.supplier s ON s.uid=w.supplier_id AND COALESCE(s.is_deleted,FALSE)=FALSE";
    private const string DisplayColumns = """
        w.uid AS "Uid", w.version AS "Version", w.code AS "Code", w.name AS "Name", w.supplier_id AS "SupplierId",
        COALESCE(s.supplier_code,w.supplier_code_snapshot) AS "SupplierCode",
        COALESCE(s.supplier_name,w.supplier_name_snapshot) AS "SupplierName", w.is_active AS "IsActive"
        """;

    /// <summary>按身份读取未软删仓库，保留停用记录供产品既有引用校验；空集合不执行 SQL。</summary>
    public async Task<IReadOnlyList<WarehouseSite>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return [];
        return (await context.Session.Connection.QueryAsync<WarehouseSite>(new CommandDefinition("""
            SELECT uid AS "Uid", is_deleted AS "IsDeleted", is_active AS "IsActive", code AS "Code", name AS "Name"
            FROM public.warehouse_site WHERE uid=ANY(@Uids) AND COALESCE(is_deleted,FALSE)=FALSE;
            """, PostgreSqlParameters.Ids(ids), context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>产品默认仓候选仅返回活动未删除仓库身份及名称，页大小上限为二百。</summary>
    public async Task<PagedResponseBase<ProductDefaultWarehouseCandidateDto>> GetProductDefaultCandidatesAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default)
    {
        var (pageIndex, pageSize, parameters) = PageParameters(request, 200);
        const string filter = """
            WHERE COALESCE(is_deleted,FALSE)=FALSE AND is_active=TRUE
              AND (CAST(@Keyword AS text) IS NULL OR code COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~'
                   OR name COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~')
            """;
        var total = await context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(1) FROM public.warehouse_site {filter};", parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        var rows = await context.Session.Connection.QueryAsync<ProductDefaultWarehouseCandidateDto>(new CommandDefinition($"""
            SELECT uid AS "Uid", code AS "Code", name AS "Name" FROM public.warehouse_site {filter}
            ORDER BY code COLLATE public.erp_dictionary_ci,uid LIMIT @PageSize OFFSET @Offset;
            """, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        return new() { Items = rows.AsList(), TotalCount = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>活动仓库选择器显示并搜索供应商实时资料或快照；缺少两者时按空串展示。</summary>
    public async Task<PagedResponseBase<WarehouseSitePickerDto>> GetPickerPagedAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
    {
        var (pageIndex, pageSize, parameters) = PageParameters(request, 200);
        const string filter = """
            WHERE COALESCE(w.is_deleted,FALSE)=FALSE AND w.is_active=TRUE
              AND (CAST(@Keyword AS text) IS NULL OR w.code COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~'
                   OR w.name COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~'
                   OR COALESCE(s.supplier_code,w.supplier_code_snapshot,'') COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~'
                   OR COALESCE(s.supplier_name,w.supplier_name_snapshot,'') COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~')
            """;
        var total = await context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(1) FROM public.warehouse_site w {SupplierJoin} {filter};", parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        var rows = await context.Session.Connection.QueryAsync<WarehouseSitePickerDto>(new CommandDefinition($"""
            SELECT w.uid AS "Uid", w.version AS "Version", w.code AS "Code", w.name AS "Name", w.supplier_id AS "SupplierId",
                   COALESCE(s.supplier_code,w.supplier_code_snapshot,'') AS "SupplierCode",
                   COALESCE(s.supplier_name,w.supplier_name_snapshot,'') AS "SupplierName", w.site_type AS "SiteType", w.is_active AS "IsActive"
            FROM public.warehouse_site w {SupplierJoin} {filter}
            ORDER BY w.code COLLATE public.erp_dictionary_ci,w.uid LIMIT @PageSize OFFSET @Offset;
            """, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        return new() { Items = rows.AsList(), TotalCount = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>供应商选择候选仅返回活动未删除供应商，页大小上限为一百，不受其仓库状态影响。</summary>
    public async Task<PagedResponseBase<WarehouseSupplierLookupDto>> SearchSupplierLookupAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
    {
        var (pageIndex, pageSize, parameters) = PageParameters(request, 100);
        const string filter = """
            WHERE COALESCE(s.is_deleted,FALSE)=FALSE AND s.is_active=TRUE
              AND (CAST(@Keyword AS text) IS NULL OR s.supplier_code COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~'
                   OR s.supplier_name COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~')
            """;
        var total = await context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(1) FROM public.supplier s {filter};", parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        var rows = await context.Session.Connection.QueryAsync<WarehouseSupplierLookupDto>(new CommandDefinition($"""
            SELECT s.uid AS "Uid", s.supplier_code AS "SupplierCode", s.supplier_name AS "SupplierName"
            FROM public.supplier s {filter} ORDER BY s.supplier_code COLLATE public.erp_dictionary_ci,s.uid LIMIT @PageSize OFFSET @Offset;
            """, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        return new() { Items = rows.AsList(), TotalCount = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>管理查询保留停用仓及可空状态筛选；显示回退快照，但关键词仅匹配供应商实时字段。</summary>
    public async Task<PagedResponseBase<WarehouseSiteDto>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default)
    {
        var (pageIndex, pageSize, parameters) = PageParameters(request, 200);
        parameters.Add("SupplierId", request.SupplierId);
        parameters.Add("IsActive", request.IsActive);
        const string filter = """
            WHERE COALESCE(w.is_deleted,FALSE)=FALSE
              AND (CAST(@SupplierId AS uuid) IS NULL OR w.supplier_id=CAST(@SupplierId AS uuid))
              AND (CAST(@IsActive AS boolean) IS NULL OR w.is_active=CAST(@IsActive AS boolean))
              AND (CAST(@Keyword AS text) IS NULL OR w.code COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~'
                   OR w.name COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~'
                   OR s.supplier_code COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~'
                   OR s.supplier_name COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~')
            """;
        var total = await context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(1) FROM public.warehouse_site w {SupplierJoin} {filter};", parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        var rows = await context.Session.Connection.QueryAsync<WarehouseSiteDto>(new CommandDefinition($"""
            SELECT {DisplayColumns}, w.updated_date AS "UpdatedDate" FROM public.warehouse_site w {SupplierJoin} {filter}
            ORDER BY w.is_active DESC,w.code COLLATE public.erp_dictionary_ci,w.uid LIMIT @PageSize OFFSET @Offset;
            """, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        return new() { Items = rows.AsList(), TotalCount = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>按身份读取未删除仓库详情，包含停用仓并保留可空供应商快照及更新时间。</summary>
    public Task<WarehouseSiteDto?> GetDtoAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("Uid", uid);
        return context.Session.Connection.QuerySingleOrDefaultAsync<WarehouseSiteDto>(new CommandDefinition($"""
            SELECT {DisplayColumns}, w.updated_date AS "UpdatedDate" FROM public.warehouse_site w {SupplierJoin}
            WHERE w.uid=@Uid AND COALESCE(w.is_deleted,FALSE)=FALSE;
            """, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
    }

    /// <summary>沿用原页号及页大小规范化，仅绑定参数；计数与取页各自克隆参数，不开启事务。</summary>
    private static (int PageIndex, int PageSize, PostgreSqlParameters Parameters) PageParameters(PagedRequestBase request, int limit)
    {
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, limit);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Keyword", string.IsNullOrWhiteSpace(request.Keyword) ? null : $"%{EscapeLike(request.Keyword.Trim())}%");
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        return (pageIndex, pageSize, parameters);
    }

    /// <summary>保持原仓库字面搜索：转义通配符及 escape 字符，空格和问号不采用公共 Build 的通配扩展。</summary>
    private static string EscapeLike(string value) => value
        .Replace("~", "~~", StringComparison.Ordinal).Replace("%", "~%", StringComparison.Ordinal)
        .Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal);
}
