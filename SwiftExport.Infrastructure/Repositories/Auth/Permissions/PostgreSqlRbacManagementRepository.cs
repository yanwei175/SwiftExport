using Dapper;
using System.Text.Json;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth.Permissions;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Auth.Permissions;

/// <summary>翻译扁平 RBAC 的十九项持久化操作；借用调用方 Serializable 事务，保留聚合版本、审计和锁顺序。</summary>
public sealed class PostgreSqlRbacManagementRepository(RepositoryBaseContext context) : IRbacManagementRepository
{
    private static readonly string Users = Table<SysUser>();
    private static readonly string Roles = Table<SysRole>();
    private static readonly string UserRoles = Table<SysUserRole>();
    private static readonly string Permissions = Table<SysPermission>();
    private static readonly string RolePermissions = Table<SysRolePermission>();
    private const string Ci = "public.erp_dictionary_ci";
    private const string RoleColumns = """
        r.uid AS "Uid", r.version AS "Version", r.role_code AS "RoleCode",
        r.role_name AS "RoleName", r.description AS "Description",
        r.is_active AS "IsActive", r.is_system AS "IsSystem"
        """;
    private const string PermissionColumns = """
        p.uid AS "Uid", p.permission_key AS "PermissionKey", p.display_name AS "DisplayName",
        p.group_key AS "GroupKey", p.sort_order AS "SortOrder", p.is_active AS "IsActive"
        """;
    private static readonly string UserColumns = $"""
        u.uid AS "Uid", u.version AS "Version", u.user_name AS "UserName",
        u.nick_name AS "NickName", u.email AS "Email", u.phone_number AS "PhoneNumber",
        u.is_active AS "IsActive",
        (u.is_active AND EXISTS(SELECT 1 FROM {UserRoles} ur
            JOIN {Roles} r ON r.uid = ur.role_id AND r.is_deleted = FALSE AND r.is_active = TRUE
            WHERE ur.user_id = u.uid AND ur.is_deleted = FALSE
              AND rtrim(r.role_code, ' ') COLLATE {Ci} = 'SYSTEM_ADMIN')) AS "IsAdmin"
        """;

    /// <summary>分页读取未删除用户含停用项；保留关键词通配、可空状态筛选和用户名 CI 排序。</summary>
    public async Task<PagedResponseBase<ManagedUserDto>> SearchUsersAsync(ManagedPageRequest request, CancellationToken cancellationToken)
    {
        var parameters = PageParameters(request);
        var filter = PageFilter("u", "user_name", "nick_name", request.Keyword?.Contains('[') == true);
        // 分开执行，避免无外层事务时由 Npgsql 批次隐式引入事务；已有事务仍由两条命令共同借用。
        var total = await context.Session.Connection.ExecuteScalarAsync<int>(Command(
            $"SELECT COUNT(*) FROM {Users} u WHERE {filter};", parameters, cancellationToken));
        var rows = await context.Session.Connection.QueryAsync<ManagedUserDto>(Command($"""
            SELECT {UserColumns} FROM {Users} u WHERE {filter}
            ORDER BY rtrim(u.user_name, ' ') COLLATE {Ci}, u.uid LIMIT @PageSize OFFSET @Offset;
            """, parameters, cancellationToken));
        return new() { Items = rows.AsList(), TotalCount = total,
            PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <summary>读取用户资料及管理员投影，不暴露密码或取得更新锁。</summary>
    public Task<ManagedUserDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        ReadUserAsync(userId, false, cancellationToken);

    /// <summary>在调用方事务中锁定目标用户聚合；应在管理员锁验之后调用。</summary>
    public Task<ManagedUserDto?> GetUserForUpdateAsync(Guid userId, CancellationToken cancellationToken) =>
        ReadUserAsync(userId, true, cancellationToken);

    /// <summary>读取有效角色关系，继续以角色标识排序，停用角色不参与投影。</summary>
    public async Task<IReadOnlyList<Guid>> GetUserRoleIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await context.Session.Connection.QueryAsync<Guid>(Command($"""
            SELECT ur.role_id FROM {UserRoles} ur
            JOIN {Roles} r ON r.uid = ur.role_id AND r.is_deleted = FALSE AND r.is_active = TRUE
            WHERE ur.user_id = @UserId AND ur.is_deleted = FALSE ORDER BY ur.role_id;
            """, new { UserId = userId }, cancellationToken))).AsList();

    /// <summary>分页读取未删除角色含停用项；显示文本 CI 匹配且编码按原排序口径比较。</summary>
    public async Task<PagedResponseBase<ManagedRoleDto>> SearchRolesAsync(ManagedPageRequest request, CancellationToken cancellationToken)
    {
        var parameters = PageParameters(request);
        var filter = PageFilter("r", "role_code", "role_name", request.Keyword?.Contains('[') == true);
        // 保留源 autocommit 的逐语句边界，不将 count 与数据读取折叠为提供者隐式批次事务。
        var total = await context.Session.Connection.ExecuteScalarAsync<int>(Command(
            $"SELECT COUNT(*) FROM {Roles} r WHERE {filter};", parameters, cancellationToken));
        var rows = await context.Session.Connection.QueryAsync<ManagedRoleDto>(Command($"""
            SELECT {RoleColumns} FROM {Roles} r WHERE {filter}
            ORDER BY rtrim(r.role_code, ' ') COLLATE {Ci}, r.uid LIMIT @PageSize OFFSET @Offset;
            """, parameters, cancellationToken));
        return new() { Items = rows.AsList(), TotalCount = total,
            PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <summary>读取未删除角色资料，不锁定普通查询。</summary>
    public Task<ManagedRoleDto?> GetRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        ReadRoleAsync(roleId, false, cancellationToken);

    /// <summary>在已有事务中锁定角色聚合并读取版本及内置标记。</summary>
    public Task<ManagedRoleDto?> GetRoleForUpdateAsync(Guid roleId, CancellationToken cancellationToken) =>
        ReadRoleAsync(roleId, true, cancellationToken);

    /// <summary>用单 UUID 数组读取有效角色，共享锁参与写入校验；空集合不访问数据库。</summary>
    public async Task<IReadOnlyList<ManagedRoleDto>> GetActiveRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.AddIds("RoleIds", roleIds);
        return (await context.Session.Connection.QueryAsync<ManagedRoleDto>(Command($"""
            SELECT {RoleColumns} FROM {Roles} r
            WHERE r.uid = ANY(@RoleIds) AND r.is_deleted = FALSE AND r.is_active = TRUE
            ORDER BY r.uid FOR SHARE OF r;
            """, parameters, cancellationToken))).AsList();
    }

    /// <summary>读取未删除且活动的权限关系，按权限标识排序。</summary>
    public async Task<IReadOnlyList<Guid>> GetRolePermissionIdsAsync(Guid roleId, CancellationToken cancellationToken) =>
        (await context.Session.Connection.QueryAsync<Guid>(Command($"""
            SELECT rp.permission_id FROM {RolePermissions} rp
            JOIN {Permissions} p ON p.uid = rp.permission_id AND p.is_deleted = FALSE AND p.is_active = TRUE
            WHERE rp.role_id = @RoleId AND rp.is_deleted = FALSE ORDER BY rp.permission_id;
            """, new { RoleId = roleId }, cancellationToken))).AsList();

    /// <summary>读取未删除权限定义，包括停用项；可分配性只由代码目录决定。</summary>
    public async Task<IReadOnlyList<ManagedPermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken)
    {
        var rows = (await context.Session.Connection.QueryAsync<ManagedPermissionDto>(Command($"""
            SELECT {PermissionColumns} FROM {Permissions} p WHERE p.is_deleted = FALSE
            ORDER BY p.sort_order, rtrim(p.permission_key, ' ') COLLATE {Ci};
            """, null, cancellationToken))).AsList();
        ApplyAssignable(rows);
        return rows;
    }

    /// <summary>按 UUID 数组共享锁定有效权限；未知目录键仍标为不可分配。</summary>
    public async Task<IReadOnlyList<ManagedPermissionDto>> GetActivePermissionsAsync(IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken)
    {
        if (permissionIds.Count == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.AddIds("PermissionIds", permissionIds);
        var rows = (await context.Session.Connection.QueryAsync<ManagedPermissionDto>(Command($"""
            SELECT {PermissionColumns} FROM {Permissions} p
            WHERE p.uid = ANY(@PermissionIds) AND p.is_deleted = FALSE AND p.is_active = TRUE
            ORDER BY p.uid FOR SHARE OF p;
            """, parameters, cancellationToken))).AsList();
        ApplyAssignable(rows);
        return rows;
    }

    /// <summary>先锁角色目录，再依序锁内置管理员角色和有效操作者并校验关系；调用方须在首次查询或写入前调用，已有快照无法刷新。</summary>
    public async Task<bool> LockAndCheckAdministratorAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        // 局部串行化 RBAC 管理事务，锁保持到外层事务结束；必须在首个 SELECT/DML 前等待，才能随后建立新快照。
        // SHARE ROW EXCLUSIVE 同时阻止其他角色目录写入；不扩大到全局业务锁，不重试或改变 Serializable。
        await context.Session.Connection.ExecuteAsync(Command(
            $"LOCK TABLE {Roles} IN SHARE ROW EXCLUSIVE MODE;", null, cancellationToken));
        var roleId = await context.Session.Connection.QuerySingleOrDefaultAsync<Guid?>(Command($"""
            SELECT r.uid FROM {Roles} r
            WHERE rtrim(r.role_code, ' ') COLLATE {Ci} = 'SYSTEM_ADMIN'
              AND r.is_system = TRUE AND r.is_active = TRUE AND r.is_deleted = FALSE FOR UPDATE OF r;
            """, null, cancellationToken));
        if (!roleId.HasValue) return false;
        var actorId = await context.Session.Connection.QuerySingleOrDefaultAsync<Guid?>(Command($"""
            SELECT actor.uid FROM {Users} actor
            WHERE actor.uid = @ActorUserId AND actor.is_active = TRUE AND actor.is_deleted = FALSE FOR UPDATE OF actor;
            """, new { ActorUserId = actorUserId }, cancellationToken));
        if (!actorId.HasValue) return false;
        // 若调用方在表锁前已建立快照，后续行锁仍可能产生 40001；本方法不刷新快照或吞掉异常。
        return await context.Session.Connection.ExecuteScalarAsync<bool>(Command($"""
            SELECT EXISTS(SELECT 1 FROM {UserRoles}
                WHERE user_id = @ActorUserId AND role_id = @AdminRoleId AND is_deleted = FALSE);
            """, new { ActorUserId = actorUserId, AdminRoleId = roleId.Value }, cancellationToken));
    }

    /// <summary>在管理员锁定后的当前事务中统计去重活动管理员，保留最后管理员规则的输入。</summary>
    public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken)
    {
        RequireTransaction();
        return context.Session.Connection.ExecuteScalarAsync<int>(Command($"""
            SELECT COUNT(DISTINCT u.uid) FROM {Users} u
            JOIN {UserRoles} ur ON ur.user_id = u.uid AND ur.is_deleted = FALSE
            JOIN {Roles} r ON r.uid = ur.role_id AND r.is_deleted = FALSE AND r.is_active = TRUE
            WHERE u.is_active = TRUE AND u.is_deleted = FALSE
              AND rtrim(r.role_code, ' ') COLLATE {Ci} = 'SYSTEM_ADMIN';
            """, null, cancellationToken));
    }

    /// <summary>按版本更新资料或启停状态；启停分支保留资料，资料分支允许清空邮箱和电话。</summary>
    public async Task<bool> UpdateUserAsync(Guid userId, int expectedVersion, string? nickName, string? email,
        string? phoneNumber, bool? isActive, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var parameters = Stamp(new SysUser());
        parameters.Add("UserId", userId); parameters.Add("ExpectedVersion", expectedVersion);
        parameters.Add("NickName", nickName); parameters.Add("Email", email);
        parameters.Add("PhoneNumber", phoneNumber); parameters.Add("IsActive", isActive);
        return await context.Session.Connection.ExecuteAsync(Command($"""
            UPDATE {Users} SET
                nick_name = CASE WHEN CAST(@IsActive AS boolean) IS NULL THEN CAST(@NickName AS text) ELSE nick_name END,
                email = CASE WHEN CAST(@IsActive AS boolean) IS NULL THEN CAST(@Email AS text) ELSE email END,
                phone_number = CASE WHEN CAST(@IsActive AS boolean) IS NULL THEN CAST(@PhoneNumber AS text) ELSE phone_number END,
                is_active = COALESCE(CAST(@IsActive AS boolean), is_active), version = version + 1,
                updated_date = @UpdatedDate, updated_by = @UpdatedBy
            WHERE uid = @UserId AND version = @ExpectedVersion AND is_deleted = FALSE;
            """, parameters, cancellationToken)) == 1;
    }

    /// <summary>先递增用户版本，再软删旧关系并插入整组新关系；失败由外层事务整体回滚。</summary>
    public async Task ReplaceUserRolesAsync(Guid userId, int expectedVersion, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var parameters = Stamp(new SysUserRole());
        parameters.Add("UserId", userId); parameters.Add("ExpectedVersion", expectedVersion);
        var changed = await context.Session.Connection.ExecuteAsync(Command($"""
            UPDATE {Users} SET version = version + 1, updated_date = @UpdatedDate, updated_by = @UpdatedBy
            WHERE uid = @UserId AND version = @ExpectedVersion AND is_deleted = FALSE;
            """, parameters, cancellationToken));
        if (changed != 1) throw new InvalidOperationException("用户版本已变化，角色替换未提交。");
        await context.Session.Connection.ExecuteAsync(Command($"""
            UPDATE {UserRoles} SET is_deleted = TRUE, version = version + 1, updated_date = @UpdatedDate, updated_by = @UpdatedBy
            WHERE user_id = @UserId AND is_deleted = FALSE;
            """, parameters, cancellationToken));
        foreach (var roleId in roleIds)
            await InsertAsync(new SysUserRole { UserId = userId, RoleId = roleId }, cancellationToken);
    }

    /// <summary>新增活动普通角色，保留生成 Uid、初始版本和审计；唯一失败不在仓储内吞掉。</summary>
    public async Task<Guid> CreateRoleAsync(string roleCode, string roleName, string? description, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var role = new SysRole { RoleCode = roleCode, RoleName = roleName, Description = description, IsActive = true, IsSystem = false };
        await InsertAsync(role, cancellationToken);
        return role.Uid;
    }

    /// <summary>按版本修改普通角色显示资料、启停或软删，不修改稳定编码或内置角色。</summary>
    public async Task<bool> UpdateRoleAsync(Guid roleId, int expectedVersion, string? roleName,
        string? description, bool? isActive, bool delete, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var parameters = Stamp(new SysRole());
        parameters.Add("RoleId", roleId); parameters.Add("ExpectedVersion", expectedVersion);
        parameters.Add("RoleName", roleName); parameters.Add("Description", description);
        parameters.Add("IsActive", isActive); parameters.Add("Delete", delete);
        return await context.Session.Connection.ExecuteAsync(Command($"""
            UPDATE {Roles} SET role_name = COALESCE(CAST(@RoleName AS text), role_name),
                description = CASE WHEN CAST(@RoleName AS text) IS NULL THEN description ELSE CAST(@Description AS text) END,
                is_active = COALESCE(CAST(@IsActive AS boolean), is_active),
                is_deleted = CASE WHEN @Delete THEN TRUE ELSE is_deleted END,
                version = version + 1, updated_date = @UpdatedDate, updated_by = @UpdatedBy
            WHERE uid = @RoleId AND version = @ExpectedVersion AND is_deleted = FALSE AND is_system = FALSE;
            """, parameters, cancellationToken)) == 1;
    }

    /// <summary>统计所有未删除用户角色关系，包含停用用户的关系，保留删除角色前置规则。</summary>
    public Task<int> CountRoleUsersAsync(Guid roleId, CancellationToken cancellationToken) =>
        context.Session.Connection.ExecuteScalarAsync<int>(Command($"""
            SELECT COUNT(*) FROM {UserRoles} WHERE role_id = @RoleId AND is_deleted = FALSE;
            """, new { RoleId = roleId }, cancellationToken));

    /// <summary>普通角色版本成功递增后软删旧权限并写入完整新集合；不绕过内置角色和版本门禁。</summary>
    public async Task ReplaceRolePermissionsAsync(Guid roleId, int expectedVersion,
        IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var parameters = Stamp(new SysRolePermission());
        parameters.Add("RoleId", roleId); parameters.Add("ExpectedVersion", expectedVersion);
        var changed = await context.Session.Connection.ExecuteAsync(Command($"""
            UPDATE {Roles} SET version = version + 1, updated_date = @UpdatedDate, updated_by = @UpdatedBy
            WHERE uid = @RoleId AND version = @ExpectedVersion AND is_deleted = FALSE AND is_system = FALSE;
            """, parameters, cancellationToken));
        if (changed != 1) throw new InvalidOperationException("角色版本已变化，权限替换未提交。");
        await context.Session.Connection.ExecuteAsync(Command($"""
            UPDATE {RolePermissions} SET is_deleted = TRUE, version = version + 1, updated_date = @UpdatedDate, updated_by = @UpdatedBy
            WHERE role_id = @RoleId AND is_deleted = FALSE;
            """, parameters, cancellationToken));
        foreach (var permissionId in permissionIds)
            await InsertAsync(new SysRolePermission { RoleId = roleId, PermissionId = permissionId }, cancellationToken);
    }

    /// <summary>按开关读取用户，锁定分支必须有事务且只锁用户聚合。</summary>
    private Task<ManagedUserDto?> ReadUserAsync(Guid id, bool locked, CancellationToken token)
    {
        if (locked) RequireTransaction();
        return context.Session.Connection.QuerySingleOrDefaultAsync<ManagedUserDto>(Command($"""
            SELECT {UserColumns} FROM {Users} u WHERE u.uid = @UserId AND u.is_deleted = FALSE
            {(locked ? "FOR UPDATE OF u" : "")};
            """, new { UserId = id }, token));
    }

    /// <summary>角色读取不机械加锁，锁定分支借用同一事务。</summary>
    private Task<ManagedRoleDto?> ReadRoleAsync(Guid id, bool locked, CancellationToken token)
    {
        if (locked) RequireTransaction();
        return context.Session.Connection.QuerySingleOrDefaultAsync<ManagedRoleDto>(Command($"""
            SELECT {RoleColumns} FROM {Roles} r WHERE r.uid = @RoleId AND r.is_deleted = FALSE
            {(locked ? "FOR UPDATE OF r" : "")};
            """, new { RoleId = id }, token));
    }

    /// <summary>通用元数据只用于本仓储已存在的角色与关系插入，不增加公共仓储能力。</summary>
    private async Task InsertAsync<T>(T entity, CancellationToken token) where T : BaseEntity
    {
        context.AuditStamp.StampForInsert(entity);
        var metadata = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
        var columns = string.Join(", ", metadata.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)));
        var values = string.Join(", ", metadata.Columns.Select(c => "@" + c.ParameterName));
        await context.Session.Connection.ExecuteAsync(Command(
            $"INSERT INTO {Table<T>()} ({columns}) VALUES ({values});", PostgreSqlParameters.Entity(entity, metadata), token));
    }

    /// <summary>更新审计时间按无时区列显式绑定，其他参数由具体方法加入。</summary>
    private PostgreSqlParameters Stamp(BaseEntity entity)
    {
        context.AuditStamp.StampForUpdate(entity);
        var parameters = new PostgreSqlParameters();
        parameters.AddTimestamp("UpdatedDate", entity.UpdatedDate);
        parameters.Add("UpdatedBy", entity.UpdatedBy);
        return parameters;
    }

    /// <summary>只拼入仓储固定列名；普通搜索保留 LIKE 快路径，字符组搜索在数据库内按位置去重匹配。</summary>
    private static string PageFilter(string alias, string first, string second, bool bracket)
    {
        // 每步恰好消费一组；UNION 将状态限制为 (组数+1)*(字符数+1)，候选位置最坏仍是组数*字符数平方。
        // 范围依照已冻结 ICU 比较，源 SQLServer 的特殊标点范围并不等同，独立实库断言继续保留该差异。
        // BETWEEN 的边界语法要求将包含 COLLATE 的整个表达式括起来；只括 JSON 提取会在真实 PG 报 42601。
        var match = bracket ? $"""
            EXISTS (SELECT 1 FROM (VALUES ({alias}.{first}), ({alias}.{second})) source(value)
                WHERE source.value IS NOT NULL AND EXISTS (
                    WITH RECURSIVE pattern AS (
                        SELECT CAST(@LikeGroups AS jsonb) AS groups
                    ), states(group_index, position) AS (
                        SELECT 0, 0
                        UNION
                        SELECT state.group_index + 1, candidate.position
                        FROM states state CROSS JOIN pattern
                        CROSS JOIN LATERAL (SELECT pattern.groups -> state.group_index AS item) current_group
                        CROSS JOIN LATERAL generate_series(state.position + 1,
                            char_length(source.value) - (jsonb_array_length(pattern.groups) - state.group_index - 1)) candidate(position)
                        WHERE state.group_index < jsonb_array_length(pattern.groups)
                          AND substring(source.value FROM state.position + 1 FOR candidate.position - state.position - 1)
                              COLLATE {Ci} LIKE (current_group.item ->> 'prefix') ESCAPE ''
                          AND (EXISTS (
                              SELECT 1 FROM jsonb_array_elements_text(current_group.item -> 'members') member(value)
                              WHERE substring(source.value FROM candidate.position FOR 1) COLLATE {Ci} = member.value
                              UNION ALL
                              SELECT 1 FROM jsonb_array_elements(current_group.item -> 'ranges') bounds(value)
                              WHERE substring(source.value FROM candidate.position FOR 1) COLLATE {Ci}
                                  BETWEEN ((bounds.value ->> 'start') COLLATE {Ci}) AND ((bounds.value ->> 'end') COLLATE {Ci})
                          ) <> CAST(current_group.item ->> 'negated' AS boolean))
                    )
                    SELECT 1 FROM states CROSS JOIN pattern
                    WHERE states.group_index = jsonb_array_length(pattern.groups)
                      AND substring(source.value FROM states.position + 1) COLLATE {Ci} LIKE CAST(@LikeTail AS text) ESCAPE ''
                ))
            """ : $"""
            {alias}.{first} COLLATE {Ci} LIKE '%' || CAST(@Keyword AS text) || '%' ESCAPE ''
            OR {alias}.{second} COLLATE {Ci} LIKE '%' || CAST(@Keyword AS text) || '%' ESCAPE ''
            """;
        return $"""
            {alias}.is_deleted = FALSE AND (CAST(@IsActive AS boolean) IS NULL OR {alias}.is_active = CAST(@IsActive AS boolean))
            AND (CAST(@Keyword AS text) IS NULL OR ({match}))
            """;
    }

    /// <summary>保留 Trim 和通配符；字符组作为 JSON 参数传递，非法或未闭合组构造成不可满足的空正集合。</summary>
    private static object PageParameters(ManagedPageRequest request)
    {
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        var groups = new List<object>();
        var pattern = "%" + keyword + "%";
        var segment = 0;
        for (var index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] != '[') continue;
            var close = pattern.IndexOf(']', index + 1);
            var negated = index + 1 < pattern.Length && pattern[index + 1] == '^';
            var members = new List<string>();
            var ranges = new List<object>();
            // 非法集合不能回退成字面搜索；空正集合永不匹配，空反集合仍消费一个字符。
            if (close < 0) { close = pattern.Length - 1; negated = false; }
            else
            {
                var content = pattern[(index + (negated ? 2 : 1))..close].EnumerateRunes().Select(r => r.ToString()).ToArray();
                for (var part = 0; part < content.Length; part++)
                {
                    // 源库只读探针：[a-b-c] 不把中间 - 当字面；[a-b--cd] 的 c 被消费而 d 保留。
                    // 已消费范围后的连续 - 标记不能当作新的起点范围；首尾 - 仍可作为成员。
                    if (content[part] == "-" && part > 0 && part < content.Length - 1)
                    {
                        if (content[part - 1] == "-") part++;
                    }
                    else if (part + 2 < content.Length && content[part + 1] == "-")
                    {
                        ranges.Add(new { start = content[part], end = content[part + 2] });
                        part += 2;
                    }
                    else if (content[part] != "-" || part == 0 || part == content.Length - 1)
                        members.Add(content[part]);
                }
            }
            groups.Add(new { prefix = pattern[segment..index], negated, members, ranges });
            index = close;
            segment = close + 1;
        }
        return new { Keyword = keyword, request.IsActive, Offset = (request.PageIndex - 1) * request.PageSize,
            request.PageSize, LikeGroups = JsonSerializer.Serialize(groups), LikeTail = pattern[segment..] };
    }

    /// <summary>可分配性来自代码目录，未知键和管理员保留键不因数据库字段而获准分配。</summary>
    private static void ApplyAssignable(IEnumerable<ManagedPermissionDto> rows)
    {
        foreach (var row in rows) row.IsAssignable = PermissionCatalog.Find(row.PermissionKey)?.IsAssignable == true;
    }

    /// <summary>防止写入和更新锁脱离调用方事务，不自行改变隔离级别。</summary>
    private void RequireTransaction()
    {
        if (context.Session.Transaction is null) throw new InvalidOperationException("RBAC 管理写入需要调用方事务。");
    }

    /// <summary>使用冻结的独立 PG 元数据定位物理表。</summary>
    private static string Table<T>()
    {
        var metadata = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
        return $"{PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)}";
    }

    /// <summary>传递调用方事务及取消，不创建或释放资源。</summary>
    private CommandDefinition Command(string sql, object? parameters, CancellationToken token) =>
        new(sql, parameters, context.Session.Transaction, cancellationToken: token);
}
