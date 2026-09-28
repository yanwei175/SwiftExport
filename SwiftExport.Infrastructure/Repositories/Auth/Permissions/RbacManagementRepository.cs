using Dapper;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth.Permissions;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Auth.Permissions;

/// <summary>在共享 DbSession 中读取扁平 RBAC，并为管理命令提供版本化、审计化写入。</summary>
public sealed class RbacManagementRepository(RepositoryBaseContext context) : IRbacManagementRepository
{
    /// <inheritdoc />
    public async Task<PagedResponseBase<ManagedUserDto>> SearchUsersAsync(ManagedPageRequest request, CancellationToken cancellationToken)
    {
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        using var grid = await context.Session.Connection.QueryMultipleAsync(Command("""
            SELECT COUNT(*) FROM dbo.SysUser u
            WHERE u.IsDeleted=0 AND (@IsActive IS NULL OR u.IsActive=@IsActive)
              AND (@Keyword IS NULL OR u.UserName LIKE N'%' + @Keyword + N'%' OR u.NickName LIKE N'%' + @Keyword + N'%');
            SELECT u.Uid,u.Version,u.UserName,u.NickName,u.Email,u.PhoneNumber,u.IsActive,
                   CAST(CASE WHEN EXISTS(
                     SELECT 1 FROM dbo.SysUser_Roles ur
                     JOIN dbo.SysRoles r ON r.Uid=ur.RoleId AND r.IsDeleted=0 AND r.IsActive=1
                     WHERE ur.UserId=u.Uid AND ur.IsDeleted=0 AND r.RoleCode=N'SYSTEM_ADMIN'
                   ) AND u.IsActive=1 THEN 1 ELSE 0 END AS bit) IsAdmin
            FROM dbo.SysUser u
            WHERE u.IsDeleted=0 AND (@IsActive IS NULL OR u.IsActive=@IsActive)
              AND (@Keyword IS NULL OR u.UserName LIKE N'%' + @Keyword + N'%' OR u.NickName LIKE N'%' + @Keyword + N'%')
            ORDER BY u.UserName,u.Uid OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, new { Keyword = keyword, request.IsActive, Offset = (request.PageIndex - 1) * request.PageSize, request.PageSize }, cancellationToken));
        var total = await grid.ReadSingleAsync<int>();
        var items = (await grid.ReadAsync<ManagedUserDto>()).AsList();
        return new PagedResponseBase<ManagedUserDto> { Items = items, TotalCount = total, PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <inheritdoc />
    public Task<ManagedUserDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        ReadUserAsync(userId, false, cancellationToken);

    /// <inheritdoc />
    public Task<ManagedUserDto?> GetUserForUpdateAsync(Guid userId, CancellationToken cancellationToken) =>
        ReadUserAsync(userId, true, cancellationToken);

    /// <summary>在需要写入时锁定用户聚合；列表读取不获取更新锁。</summary>
    private Task<ManagedUserDto?> ReadUserAsync(Guid userId, bool forUpdate, CancellationToken cancellationToken)
    {
        var lockHint = forUpdate ? "WITH(UPDLOCK,HOLDLOCK)" : "";
        return context.Session.Connection.QuerySingleOrDefaultAsync<ManagedUserDto>(Command($"""
            SELECT u.Uid,u.Version,u.UserName,u.NickName,u.Email,u.PhoneNumber,u.IsActive,
                   CAST(CASE WHEN EXISTS(
                     SELECT 1 FROM dbo.SysUser_Roles ur
                     JOIN dbo.SysRoles r ON r.Uid=ur.RoleId AND r.IsDeleted=0 AND r.IsActive=1
                     WHERE ur.UserId=u.Uid AND ur.IsDeleted=0 AND r.RoleCode=N'SYSTEM_ADMIN'
                   ) AND u.IsActive=1 THEN 1 ELSE 0 END AS bit) IsAdmin
            FROM dbo.SysUser u {lockHint}
            WHERE u.Uid=@UserId AND u.IsDeleted=0;
            """, new { UserId = userId }, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetUserRoleIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await context.Session.Connection.QueryAsync<Guid>(Command("""
            SELECT ur.RoleId FROM dbo.SysUser_Roles ur
            JOIN dbo.SysRoles r ON r.Uid=ur.RoleId AND r.IsDeleted=0 AND r.IsActive=1
            WHERE ur.UserId=@UserId AND ur.IsDeleted=0 ORDER BY ur.RoleId;
            """, new { UserId = userId }, cancellationToken));
        return rows.AsList();
    }

    /// <inheritdoc />
    public async Task<PagedResponseBase<ManagedRoleDto>> SearchRolesAsync(ManagedPageRequest request, CancellationToken cancellationToken)
    {
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        using var grid = await context.Session.Connection.QueryMultipleAsync(Command("""
            SELECT COUNT(*) FROM dbo.SysRoles r
            WHERE r.IsDeleted=0 AND (@IsActive IS NULL OR r.IsActive=@IsActive)
              AND (@Keyword IS NULL OR r.RoleCode LIKE N'%' + @Keyword + N'%' OR r.RoleName LIKE N'%' + @Keyword + N'%');
            SELECT r.Uid,r.Version,r.RoleCode,r.RoleName,r.Description,r.IsActive,r.IsSystem
            FROM dbo.SysRoles r
            WHERE r.IsDeleted=0 AND (@IsActive IS NULL OR r.IsActive=@IsActive)
              AND (@Keyword IS NULL OR r.RoleCode LIKE N'%' + @Keyword + N'%' OR r.RoleName LIKE N'%' + @Keyword + N'%')
            ORDER BY r.RoleCode,r.Uid OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, new { Keyword = keyword, request.IsActive, Offset = (request.PageIndex - 1) * request.PageSize, request.PageSize }, cancellationToken));
        var total = await grid.ReadSingleAsync<int>();
        var items = (await grid.ReadAsync<ManagedRoleDto>()).AsList();
        return new PagedResponseBase<ManagedRoleDto> { Items = items, TotalCount = total, PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <inheritdoc />
    public Task<ManagedRoleDto?> GetRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        ReadRoleAsync(roleId, false, cancellationToken);

    /// <inheritdoc />
    public Task<ManagedRoleDto?> GetRoleForUpdateAsync(Guid roleId, CancellationToken cancellationToken) =>
        ReadRoleAsync(roleId, true, cancellationToken);

    /// <summary>在管理写入时锁定目标角色并读取当前版本及内置标志。</summary>
    private Task<ManagedRoleDto?> ReadRoleAsync(Guid roleId, bool forUpdate, CancellationToken cancellationToken)
    {
        var lockHint = forUpdate ? "WITH(UPDLOCK,HOLDLOCK)" : "";
        return context.Session.Connection.QuerySingleOrDefaultAsync<ManagedRoleDto>(Command($"""
            SELECT Uid,Version,RoleCode,RoleName,Description,IsActive,IsSystem
            FROM dbo.SysRoles {lockHint} WHERE Uid=@RoleId AND IsDeleted=0;
            """, new { RoleId = roleId }, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ManagedRoleDto>> GetActiveRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0) return [];
        var rows = await context.Session.Connection.QueryAsync<ManagedRoleDto>(Command("""
            SELECT Uid,Version,RoleCode,RoleName,Description,IsActive,IsSystem
            FROM dbo.SysRoles WITH(HOLDLOCK)
            WHERE Uid IN @RoleIds AND IsDeleted=0 AND IsActive=1;
            """, new { RoleIds = roleIds }, cancellationToken));
        return rows.AsList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetRolePermissionIdsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var rows = await context.Session.Connection.QueryAsync<Guid>(Command("""
            SELECT rp.PermissionId FROM dbo.SysRole_Permissions rp
            JOIN dbo.SysPermissionDefinitions p ON p.Uid=rp.PermissionId AND p.IsDeleted=0 AND p.IsActive=1
            WHERE rp.RoleId=@RoleId AND rp.IsDeleted=0 ORDER BY rp.PermissionId;
            """, new { RoleId = roleId }, cancellationToken));
        return rows.AsList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ManagedPermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken)
    {
        var rows = (await context.Session.Connection.QueryAsync<ManagedPermissionDto>(Command("""
            SELECT Uid,PermissionKey,DisplayName,GroupKey,SortOrder,IsActive
            FROM dbo.SysPermissionDefinitions WHERE IsDeleted=0 ORDER BY SortOrder,PermissionKey;
            """, null, cancellationToken))).AsList();
        ApplyAssignable(rows);
        return rows;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ManagedPermissionDto>> GetActivePermissionsAsync(IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken)
    {
        if (permissionIds.Count == 0) return [];
        var rows = (await context.Session.Connection.QueryAsync<ManagedPermissionDto>(Command("""
            SELECT Uid,PermissionKey,DisplayName,GroupKey,SortOrder,IsActive
            FROM dbo.SysPermissionDefinitions WITH(HOLDLOCK)
            WHERE Uid IN @PermissionIds AND IsDeleted=0 AND IsActive=1;
            """, new { PermissionIds = permissionIds }, cancellationToken))).AsList();
        ApplyAssignable(rows);
        return rows;
    }

    /// <summary>可分配标志只从代码目录取得，数据库中的显示资料不能改变授权边界。</summary>
    private static void ApplyAssignable(IEnumerable<ManagedPermissionDto> rows)
    {
        foreach (var row in rows)
            row.IsAssignable = PermissionCatalog.Find(row.PermissionKey)?.IsAssignable == true;
    }

    /// <inheritdoc />
    public async Task<bool> LockAndCheckAdministratorAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        // 所有管理写命令先锁同一内置角色行，再锁目标聚合；并发互撤管理员由此串行化。
        return await context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            DECLARE @AdminRoleId uniqueidentifier;
            SELECT @AdminRoleId=Uid FROM dbo.SysRoles WITH(UPDLOCK,HOLDLOCK)
            WHERE RoleCode=N'SYSTEM_ADMIN' AND IsSystem=1 AND IsActive=1 AND IsDeleted=0;
            SELECT CAST(CASE WHEN @AdminRoleId IS NOT NULL AND EXISTS(
              SELECT 1 FROM dbo.SysUser actor WITH(UPDLOCK,HOLDLOCK)
              JOIN dbo.SysUser_Roles ur WITH(HOLDLOCK) ON ur.UserId=actor.Uid AND ur.IsDeleted=0
              WHERE actor.Uid=@ActorUserId AND actor.IsActive=1 AND actor.IsDeleted=0
                AND ur.RoleId=@AdminRoleId)
              THEN 1 ELSE 0 END AS bit);
            """, new { ActorUserId = actorUserId }, cancellationToken));
    }

    /// <inheritdoc />
    public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken)
    {
        RequireTransaction();
        return context.Session.Connection.ExecuteScalarAsync<int>(Command("""
            SELECT COUNT(DISTINCT u.Uid) FROM dbo.SysUser u WITH(HOLDLOCK)
            JOIN dbo.SysUser_Roles ur WITH(HOLDLOCK) ON ur.UserId=u.Uid AND ur.IsDeleted=0
            JOIN dbo.SysRoles r WITH(HOLDLOCK) ON r.Uid=ur.RoleId AND r.IsDeleted=0 AND r.IsActive=1
            WHERE u.IsActive=1 AND u.IsDeleted=0 AND r.RoleCode=N'SYSTEM_ADMIN';
            """, null, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<bool> UpdateUserAsync(Guid userId, int expectedVersion, string? nickName, string? email, string? phoneNumber, bool? isActive, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var stamp = new SysUser();
        context.AuditStamp.StampForUpdate(stamp);
        var rows = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.SysUser SET
              NickName=CASE WHEN @IsActive IS NULL THEN @NickName ELSE NickName END,
              Email=CASE WHEN @IsActive IS NULL THEN @Email ELSE Email END,
              PhoneNumber=CASE WHEN @IsActive IS NULL THEN @PhoneNumber ELSE PhoneNumber END,
              IsActive=COALESCE(@IsActive,IsActive),Version=Version+1,
              UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@UserId AND Version=@ExpectedVersion AND IsDeleted=0;
            """, new { UserId = userId, ExpectedVersion = expectedVersion, NickName = nickName, Email = email, PhoneNumber = phoneNumber, IsActive = isActive, stamp.UpdatedDate, stamp.UpdatedBy }, cancellationToken));
        return rows == 1;
    }

    /// <inheritdoc />
    public async Task ReplaceUserRolesAsync(Guid userId, int expectedVersion, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var stamp = new SysUserRole();
        context.AuditStamp.StampForUpdate(stamp);
        var changed = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.SysUser SET Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@UserId AND Version=@ExpectedVersion AND IsDeleted=0;
            """, new { UserId = userId, ExpectedVersion = expectedVersion, stamp.UpdatedDate, stamp.UpdatedBy }, cancellationToken));
        if (changed != 1)
            throw new InvalidOperationException("用户版本已变化，角色替换未提交。");
        await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.SysUser_Roles SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE UserId=@UserId AND IsDeleted=0;
            """, new { UserId = userId, stamp.UpdatedDate, stamp.UpdatedBy }, cancellationToken));
        foreach (var roleId in roleIds)
        {
            var relation = new SysUserRole { UserId = userId, RoleId = roleId };
            context.AuditStamp.StampForInsert(relation);
            await context.Session.Connection.ExecuteAsync(Command("""
                INSERT dbo.SysUser_Roles(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,UserId,RoleId)
                VALUES(@Uid,@Version,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@UserId,@RoleId);
                """, relation, cancellationToken));
        }
    }

    /// <inheritdoc />
    public async Task<Guid> CreateRoleAsync(string roleCode, string roleName, string? description, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var role = new SysRole { RoleCode = roleCode, RoleName = roleName, Description = description, IsActive = true, IsSystem = false };
        context.AuditStamp.StampForInsert(role);
        await context.Session.Connection.ExecuteAsync(Command("""
            INSERT dbo.SysRoles(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
              RoleCode,RoleName,Description,IsActive,IsSystem)
            VALUES(@Uid,@Version,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
              @RoleCode,@RoleName,@Description,1,0);
            """, role, cancellationToken));
        return role.Uid;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateRoleAsync(Guid roleId, int expectedVersion, string? roleName, string? description, bool? isActive, bool delete, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var stamp = new SysRole();
        context.AuditStamp.StampForUpdate(stamp);
        var rows = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.SysRoles SET
              RoleName=COALESCE(@RoleName,RoleName),
              Description=CASE WHEN @RoleName IS NULL THEN Description ELSE @Description END,
              IsActive=COALESCE(@IsActive,IsActive),
              IsDeleted=CASE WHEN @Delete=1 THEN 1 ELSE IsDeleted END,
              Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@RoleId AND Version=@ExpectedVersion AND IsDeleted=0 AND IsSystem=0;
            """, new { RoleId = roleId, ExpectedVersion = expectedVersion, RoleName = roleName, Description = description, IsActive = isActive, Delete = delete, stamp.UpdatedDate, stamp.UpdatedBy }, cancellationToken));
        return rows == 1;
    }

    /// <inheritdoc />
    public Task<int> CountRoleUsersAsync(Guid roleId, CancellationToken cancellationToken) =>
        context.Session.Connection.ExecuteScalarAsync<int>(Command("""
            SELECT COUNT(*) FROM dbo.SysUser_Roles WITH(HOLDLOCK)
            WHERE RoleId=@RoleId AND IsDeleted=0;
            """, new { RoleId = roleId }, cancellationToken));

    /// <inheritdoc />
    public async Task ReplaceRolePermissionsAsync(Guid roleId, int expectedVersion, IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var stamp = new SysRolePermission();
        context.AuditStamp.StampForUpdate(stamp);
        var changed = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.SysRoles SET Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@RoleId AND Version=@ExpectedVersion AND IsDeleted=0 AND IsSystem=0;
            """, new { RoleId = roleId, ExpectedVersion = expectedVersion, stamp.UpdatedDate, stamp.UpdatedBy }, cancellationToken));
        if (changed != 1)
            throw new InvalidOperationException("角色版本已变化，权限替换未提交。");
        await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.SysRole_Permissions SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE RoleId=@RoleId AND IsDeleted=0;
            """, new { RoleId = roleId, stamp.UpdatedDate, stamp.UpdatedBy }, cancellationToken));
        foreach (var permissionId in permissionIds)
        {
            var relation = new SysRolePermission { RoleId = roleId, PermissionId = permissionId };
            context.AuditStamp.StampForInsert(relation);
            await context.Session.Connection.ExecuteAsync(Command("""
                INSERT dbo.SysRole_Permissions(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,RoleId,PermissionId)
                VALUES(@Uid,@Version,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@RoleId,@PermissionId);
                """, relation, cancellationToken));
        }
    }

    /// <summary>防止仓储写入脱离 Application 控制的共享事务。</summary>
    private void RequireTransaction()
    {
        if (context.Session.Transaction is null)
            throw new InvalidOperationException("RBAC 管理写入需要调用方事务。");
    }

    /// <summary>统一绑定当前请求连接、事务与取消令牌。</summary>
    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, context.Session.Transaction, cancellationToken: cancellationToken);
}
