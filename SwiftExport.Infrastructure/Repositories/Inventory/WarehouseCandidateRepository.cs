using Dapper;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory;

/// <summary>采购仓库候选专用读取，保留既有仓库管理查询的历史及停用展示语义。</summary>
public sealed class WarehouseCandidateRepository(RepositoryBaseContext context) : IWarehouseCandidateRepository
{
    // 与采购最终校验的 IsNullOrWhiteSpace 对齐，不把纯空白编码/名称当合法身份资料。
    private const string Whitespace = "\u0009\u000A\u000B\u000C\u000D\u0020\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000";

    /// <summary>重验真实有效账号和角色关联；只读角色不允许新增选择，不修改全局授权框架。</summary>
    public Task<bool> HasPermissionAsync(Guid userId, CancellationToken cancellationToken)
        => context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CAST(CASE WHEN EXISTS(
                SELECT 1 FROM dbo.SysUser u WITH(HOLDLOCK)
                JOIN dbo.SysUser_Roles ur WITH(HOLDLOCK) ON ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0
                JOIN dbo.SysRoles r WITH(HOLDLOCK) ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0
                WHERE u.Uid=@UserId AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
                  AND r.RoleName IN(N'Admin',N'Procurement.Order.Create',N'Procurement.Order.Confirm')
            ) THEN 1 ELSE 0 END AS bit);
            """, new { UserId = userId }, cancellationToken));

    /// <summary>仓库多对一关联真实供应商，仅返回双方启用且资料完整的虚拟仓；不存在归属时不以快照补资格。</summary>
    public async Task<PagedResponseBase<WarehouseCandidateDto>> SearchAsync(
        PagedRequestBase request, CancellationToken cancellationToken)
    {
        const string filter = """
            FROM dbo.WarehouseSite w
            JOIN dbo.Supplier s ON s.Uid=w.SupplierId AND ISNULL(s.IsDeleted,0)=0
            WHERE ISNULL(w.IsDeleted,0)=0 AND w.IsActive=1 AND w.SiteType=@SiteType AND s.IsActive=1
              AND w.Uid<>@EmptyUid AND s.Uid<>@EmptyUid
              AND NULLIF(TRIM(@Whitespace FROM w.Code),N'') IS NOT NULL
              AND NULLIF(TRIM(@Whitespace FROM w.Name),N'') IS NOT NULL
              AND NULLIF(TRIM(@Whitespace FROM s.SupplierCode),N'') IS NOT NULL
              AND NULLIF(TRIM(@Whitespace FROM s.SupplierName),N'') IS NOT NULL
              AND (@Keyword IS NULL OR w.Code LIKE @Keyword ESCAPE N'~' OR w.Name LIKE @Keyword ESCAPE N'~'
                   OR s.SupplierCode LIKE @Keyword ESCAPE N'~' OR s.SupplierName LIKE @Keyword ESCAPE N'~')
            """;
        // 仅拼接固定 SQL，搜索值参数化并按字面转义通配符；不默认限制为采购实际供应商。
        var keyword = string.IsNullOrEmpty(request.Keyword) ? null
            : "%" + request.Keyword.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
        using var grid = await context.Session.Connection.QueryMultipleAsync(Command(
            "SELECT COUNT(*) " + filter + "; SELECT w.Uid,w.Version,w.Code,w.Name,w.SiteType,w.IsActive,"
            + "s.Uid AS SupplierId,s.SupplierCode,s.SupplierName,s.IsActive AS SupplierIsActive " + filter
            + " ORDER BY w.Code,w.Uid OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;",
            new { Keyword = keyword, Whitespace, EmptyUid = Guid.Empty, SiteType = WarehouseSiteTypeEnum.SupplierVirtual,
                Skip = (long)(request.PageIndex - 1) * request.PageSize, Take = request.PageSize }, cancellationToken));
        var count = await grid.ReadSingleAsync<int>();
        var items = (await grid.ReadAsync<WarehouseCandidateDto>()).ToList();
        return new PagedResponseBase<WarehouseCandidateDto>
        {
            Items = items, TotalCount = count, PageIndex = request.PageIndex, PageSize = request.PageSize
        };
    }

    /// <summary>全部命令加入调用方只读事务并透传取消，计数与分页不会各自脱离授权范围。</summary>
    private CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken)
        => new(sql, parameters, context.Session.Transaction ?? throw new InvalidOperationException("候选查询需要只读事务。"),
            cancellationToken: cancellationToken);
}
