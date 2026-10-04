using Dapper;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth.Permissions;
using SwiftExport.Core.Models.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Auth.Permissions;

/// <summary>在共享 PostgreSQL 会话中读取活动授权事实；单条查询保持身份、角色和权限的快照一致性。</summary>
public sealed class PostgreSqlUserPermissionRepository(RepositoryBaseContext context) : IUserPermissionRepository
{
    private static readonly string UserTable = QualifiedTable(typeof(SysUser));
    private static readonly string UserRoleTable = QualifiedTable(typeof(SysUserRole));
    private static readonly string RoleTable = QualifiedTable(typeof(SysRole));
    private static readonly string RolePermissionTable = QualifiedTable(typeof(SysRolePermission));
    private static readonly string PermissionTable = QualifiedTable(typeof(SysPermission));

    /// <summary>在一次 SELECT 中读取活动账号及去重、排序后的授权集合；无角色账号保留，账号无效时返回空。</summary>
    public async Task<UserPermissionFacts?> GetPermissionFactsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("UserId", userId);
        // 活动条件放在 LEFT JOIN 内，避免无角色或无权限的有效账号被过滤掉。
        // 集合由数据库按目标列比较规则去重和排序，不在客户端另换比较口径。
        // 仅空值判定去除尾部普通空格，保留 SQL Server 空字符串比较边界，不改写返回的显示名称。
        var row = await context.Session.Connection.QuerySingleOrDefaultAsync<PermissionFactsRow>(Command($"""
            SELECT u.uid AS "UserId", TRUE AS "IsActive",
                   COALESCE(bool_or(r.role_code = 'SYSTEM_ADMIN'), FALSE) AS "IsAdmin",
                   COALESCE(array_agg(DISTINCT r.role_name::text ORDER BY r.role_name::text)
                       FILTER (WHERE NULLIF(rtrim(r.role_name, ' '), '') IS NOT NULL), ARRAY[]::text[]) AS "RoleNames",
                   COALESCE(array_agg(DISTINCT r.role_code::text ORDER BY r.role_code::text)
                       FILTER (WHERE r.uid IS NOT NULL), ARRAY[]::text[]) AS "RoleCodes",
                   COALESCE(array_agg(DISTINCT p.permission_key::text ORDER BY p.permission_key::text)
                       FILTER (WHERE p.uid IS NOT NULL), ARRAY[]::text[]) AS "PermissionKeys"
            FROM {UserTable} u
            LEFT JOIN {UserRoleTable} ur ON ur.user_id = u.uid AND COALESCE(ur.is_deleted, FALSE) = FALSE
            LEFT JOIN {RoleTable} r ON r.uid = ur.role_id AND COALESCE(r.is_deleted, FALSE) = FALSE AND r.is_active = TRUE
            LEFT JOIN {RolePermissionTable} rp ON rp.role_id = r.uid AND COALESCE(rp.is_deleted, FALSE) = FALSE
            LEFT JOIN {PermissionTable} p ON p.uid = rp.permission_id AND COALESCE(p.is_deleted, FALSE) = FALSE AND p.is_active = TRUE
            WHERE u.uid = @UserId AND u.is_active = TRUE AND COALESCE(u.is_deleted, FALSE) = FALSE
            GROUP BY u.uid;
            """, parameters, cancellationToken));
        return row is null ? null : new UserPermissionFacts
        {
            UserId = row.UserId,
            IsActive = row.IsActive,
            IsAdmin = row.IsAdmin,
            RoleNames = row.RoleNames,
            RoleCodes = row.RoleCodes,
            PermissionKeys = row.PermissionKeys
        };
    }

    /// <summary>读取管理员选择目标所需的活动账号摘要；空昵称回退为用户名，不返回密码凭据。</summary>
    public async Task<IReadOnlyList<UserSummaryDto>> GetActiveUsersAsync(CancellationToken cancellationToken = default)
    {
        // 空昵称包含仅有普通空格的文本；非空昵称仍原样返回，不裁剪用户资料。
        var rows = await context.Session.Connection.QueryAsync<UserSummaryDto>(Command($"""
            SELECT u.uid AS "Uid", u.user_name AS "UserName",
                   CASE WHEN NULLIF(rtrim(u.nick_name, ' '), '') IS NULL THEN u.user_name ELSE u.nick_name END AS "NickName",
                   EXISTS(
                       SELECT 1 FROM {UserRoleTable} ur
                       JOIN {RoleTable} r ON r.uid = ur.role_id AND COALESCE(r.is_deleted, FALSE) = FALSE AND r.is_active = TRUE
                       WHERE ur.user_id = u.uid AND COALESCE(ur.is_deleted, FALSE) = FALSE AND r.role_code = 'SYSTEM_ADMIN'
                   ) AS "IsAdmin"
            FROM {UserTable} u
            WHERE u.is_active = TRUE AND COALESCE(u.is_deleted, FALSE) = FALSE
            ORDER BY u.user_name, u.uid;
            """, null, cancellationToken));
        return rows.AsList();
    }

    /// <summary>复用独立实体映射取得物理表名，保持 SQL Server 元数据不变。</summary>
    private static string QualifiedTable(Type entity)
    {
        var metadata = PostgreSqlEntityMetadata.GetMetadata(entity);
        return $"{PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)}";
    }

    /// <summary>统一传递调用方事务和取消令牌，不创建或接管事务。</summary>
    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, context.Session.Transaction, cancellationToken: cancellationToken);

    /// <summary>接收单条查询的 PostgreSQL 文本数组，再组装既有权限事实契约。</summary>
    private sealed class PermissionFactsRow
    {
        public Guid UserId { get; set; }
        public bool IsActive { get; set; }
        public bool IsAdmin { get; set; }
        public string[] RoleNames { get; set; } = [];
        public string[] RoleCodes { get; set; } = [];
        public string[] PermissionKeys { get; set; } = [];
    }
}
