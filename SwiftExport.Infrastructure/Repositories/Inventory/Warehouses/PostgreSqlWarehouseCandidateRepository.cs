using Dapper;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;

/// <summary>PostgreSQL 采购仓库候选只读实现；以真实供应商资格筛选，不替换 SQL Server 默认注册。</summary>
public sealed class PostgreSqlWarehouseCandidateRepository(RepositoryBaseContext context) : IWarehouseCandidateRepository
{
    // 精确对应 IsNullOrWhiteSpace 字符集合；资格判断使用 C 比较，不能让语言排序规则忽略非空白字符。
    private const string Whitespace = "\u0009\u000A\u000B\u000C\u000D\u0020\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000";

    /// <summary>在调用方事务内返回双方启用且身份完整的虚拟仓；分页和关键词上限沿用 Application 已验证的请求。</summary>
    public async Task<PagedResponseBase<WarehouseCandidateDto>> SearchAsync(PagedRequestBase request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = context.Session.Transaction ?? throw new InvalidOperationException("候选查询需要只读事务。");
        const string filter = """
            FROM public.warehouse_site w
            JOIN public.supplier s ON s.uid=w.supplier_id AND COALESCE(s.is_deleted,FALSE)=FALSE
            WHERE COALESCE(w.is_deleted,FALSE)=FALSE AND w.is_active=TRUE AND w.site_type=@SiteType AND s.is_active=TRUE
              AND w.uid<>@EmptyUid AND s.uid<>@EmptyUid
              AND btrim(w.code COLLATE "C",@Whitespace) <> '' COLLATE "C"
              AND btrim(w.name COLLATE "C",@Whitespace) <> '' COLLATE "C"
              AND btrim(s.supplier_code COLLATE "C",@Whitespace) <> '' COLLATE "C"
              AND btrim(s.supplier_name COLLATE "C",@Whitespace) <> '' COLLATE "C"
              AND (CAST(@Keyword AS text) IS NULL
                   OR w.code COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~'
                   OR w.name COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~'
                   OR s.supplier_code COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~'
                   OR s.supplier_name COLLATE public.erp_dictionary_ci LIKE CAST(@Keyword AS text) ESCAPE '~')
            """;
        // 与原仓储一致：只按字面搜索通配字符，不在仓储再次 Trim、放宽页大小或按快照补造候选。
        var keyword = string.IsNullOrEmpty(request.Keyword) ? null
            : "%" + request.Keyword.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
        var parameters = new PostgreSqlParameters();
        parameters.Add("Keyword", keyword);
        parameters.Add("Whitespace", Whitespace);
        parameters.Add("EmptyUid", Guid.Empty);
        parameters.Add("SiteType", (int)WarehouseSiteTypeEnum.SupplierVirtual);
        parameters.Add("Skip", (long)(request.PageIndex - 1) * request.PageSize);
        parameters.Add("Take", request.PageSize);
        var count = await context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) " + filter, parameters, transaction, cancellationToken: cancellationToken));
        var items = await context.Session.Connection.QueryAsync<WarehouseCandidateDto>(new CommandDefinition("""
            SELECT w.uid AS "Uid", w.version AS "Version", w.code AS "Code", w.name AS "Name",
                   w.site_type AS "SiteType", w.is_active AS "IsActive", s.uid AS "SupplierId",
                   s.supplier_code AS "SupplierCode", s.supplier_name AS "SupplierName", s.is_active AS "SupplierIsActive"
            """ + "\n" + filter + " ORDER BY w.code COLLATE public.erp_dictionary_ci,w.uid LIMIT @Take OFFSET @Skip;",
            parameters, transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<WarehouseCandidateDto>
        {
            Items = items.AsList(), TotalCount = count, PageIndex = request.PageIndex, PageSize = request.PageSize
        };
    }
}
