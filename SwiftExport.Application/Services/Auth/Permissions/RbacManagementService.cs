using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using SwiftExport.Core.Interfaces.Data;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auth.Permissions;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auditing;

namespace SwiftExport.Application.Services.Auth.Permissions;

/// <summary>编排管理员 RBAC 查询与事务命令，统一核验操作者、目标版本及最后管理员约束。</summary>
public sealed class RbacManagementService(
    ServiceBaseContext context,
    IRbacManagementRepository repository,
    IPermissionEvaluationService permissionEvaluation,
    IBusinessOperationLogRepository auditLogs,
    ICurrentUser currentUser) : IRbacManagementService
{
    /// <inheritdoc />
    public Task<ServiceResult<PagedResponseBase<ManagedUserDto>>> SearchUsersAsync(ManagedPageRequest request, CancellationToken cancellationToken) =>
        ReadAsync("System.Users.View", async () =>
        {
            if (!ValidPage(request)) return Validation<PagedResponseBase<ManagedUserDto>>("分页参数无效。", "PageIndex");
            return ServiceResult<PagedResponseBase<ManagedUserDto>>.Ok(await repository.SearchUsersAsync(request, cancellationToken));
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<ManagedUserDto>> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        ReadAsync("System.Users.View", async () =>
            userId == Guid.Empty ? Validation<ManagedUserDto>("用户标识无效。", "UserId")
            : FromNullable(await repository.GetUserAsync(userId, cancellationToken), "用户不存在。"), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<ManagedUserDto>> UpdateUserAsync(Guid userId, UpdateManagedUserRequest request, CancellationToken cancellationToken) =>
        WriteAsync(async () =>
        {
            if (userId == Guid.Empty || request is null || request.ExpectedVersion < 1)
                return Validation<ManagedUserDto>("用户和预期版本无效。", "ExpectedVersion");
            var target = await repository.GetUserForUpdateAsync(userId, cancellationToken);
            if (target is null) return Blocked<ManagedUserDto>("用户不存在。");
            if (target.Version != request.ExpectedVersion) return Conflict<ManagedUserDto>();
            var nickName = request.NickName?.Trim();
            if (string.IsNullOrWhiteSpace(nickName)) return Validation<ManagedUserDto>("显示名称不能为空。", "NickName");
            if (!await repository.UpdateUserAsync(userId, request.ExpectedVersion, nickName,
                    request.Email?.Trim(), request.PhoneNumber?.Trim(), null, cancellationToken))
                return Conflict<ManagedUserDto>();
            var updated = (await repository.GetUserAsync(userId, cancellationToken))!;
            await AuditAsync("UpdateUser", "修改用户资料", userId, target, updated, cancellationToken);
            return ServiceResult<ManagedUserDto>.Ok(updated);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<ManagedUserDto>> SetUserStatusAsync(Guid userId, SetManagedStatusRequest request, CancellationToken cancellationToken) =>
        WriteAsync(async () =>
        {
            if (userId == Guid.Empty || request is null || request.ExpectedVersion < 1)
                return Validation<ManagedUserDto>("用户和预期版本无效。", "ExpectedVersion");
            var target = await repository.GetUserForUpdateAsync(userId, cancellationToken);
            if (target is null) return Blocked<ManagedUserDto>("用户不存在。");
            if (target.Version != request.ExpectedVersion) return Conflict<ManagedUserDto>();
            if (target.IsAdmin && !request.IsActive && await repository.CountActiveAdministratorsAsync(cancellationToken) <= 1)
                return Blocked<ManagedUserDto>("不能停用最后一位有效管理员。");
            if (!await repository.UpdateUserAsync(userId, request.ExpectedVersion, null, null, null, request.IsActive, cancellationToken))
                return Conflict<ManagedUserDto>();
            var updated = (await repository.GetUserAsync(userId, cancellationToken))!;
            await AuditAsync("SetUserStatus", "修改用户状态", userId, target, updated, cancellationToken);
            return ServiceResult<ManagedUserDto>.Ok(updated);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<UserRolesDto>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken) =>
        ReadAsync("System.Users.View", async () =>
        {
            var user = await repository.GetUserAsync(userId, cancellationToken);
            if (user is null) return Blocked<UserRolesDto>("用户不存在。");
            return ServiceResult<UserRolesDto>.Ok(new UserRolesDto
            {
                UserId = userId, Version = user.Version,
                RoleIds = (await repository.GetUserRoleIdsAsync(userId, cancellationToken)).ToList()
            });
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<UserRolesDto>> ReplaceUserRolesAsync(Guid userId, ReplaceUserRolesRequest request, CancellationToken cancellationToken) =>
        WriteAsync(async () =>
        {
            if (userId == Guid.Empty || request?.RoleIds is null || request.ExpectedVersion < 1)
                return Validation<UserRolesDto>("用户、角色集合或预期版本无效。", "RoleIds");
            if (HasDuplicateOrEmpty(request.RoleIds)) return Validation<UserRolesDto>("角色标识为空或重复。", "RoleIds");
            var target = await repository.GetUserForUpdateAsync(userId, cancellationToken);
            if (target is null || !target.IsActive) return Blocked<UserRolesDto>("目标用户不存在或已停用。");
            if (target.Version != request.ExpectedVersion) return Conflict<UserRolesDto>();
            var roles = await repository.GetActiveRolesAsync(request.RoleIds, cancellationToken);
            if (roles.Count != request.RoleIds.Count) return Validation<UserRolesDto>("角色不存在、已停用或已删除。", "RoleIds");
            var retainsAdmin = roles.Any(role => role.IsSystem && role.RoleCode == SystemRoleCodes.SystemAdmin);
            if (target.IsAdmin && !retainsAdmin && await repository.CountActiveAdministratorsAsync(cancellationToken) <= 1)
                return Blocked<UserRolesDto>("不能撤销最后一位有效管理员的角色。");
            var before = await repository.GetUserRoleIdsAsync(userId, cancellationToken);
            await repository.ReplaceUserRolesAsync(userId, request.ExpectedVersion, request.RoleIds, cancellationToken);
            await AuditAsync("ReplaceUserRoles", "用户角色整组替换", userId, before, request.RoleIds, cancellationToken);
            return ServiceResult<UserRolesDto>.Ok(new UserRolesDto
            {
                UserId = userId, Version = request.ExpectedVersion + 1, RoleIds = request.RoleIds.ToList()
            });
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<List<string>>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
        ReadAsync("System.Users.View", async () =>
        {
            var user = await repository.GetUserAsync(userId, cancellationToken);
            if (user is null) return Blocked<List<string>>("用户不存在。");
            var projection = await permissionEvaluation.GetProjectionAsync(userId, cancellationToken);
            return ServiceResult<List<string>>.Ok(projection?.Permissions.ToList() ?? []);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<PagedResponseBase<ManagedRoleDto>>> SearchRolesAsync(ManagedPageRequest request, CancellationToken cancellationToken) =>
        ReadAsync("System.Roles.View", async () =>
        {
            if (!ValidPage(request)) return Validation<PagedResponseBase<ManagedRoleDto>>("分页参数无效。", "PageIndex");
            return ServiceResult<PagedResponseBase<ManagedRoleDto>>.Ok(await repository.SearchRolesAsync(request, cancellationToken));
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<ManagedRoleDto>> GetRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        ReadAsync("System.Roles.View", async () =>
            roleId == Guid.Empty ? Validation<ManagedRoleDto>("角色标识无效。", "RoleId")
            : FromNullable(await repository.GetRoleAsync(roleId, cancellationToken), "角色不存在。"), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<ManagedRoleDto>> CreateRoleAsync(CreateManagedRoleRequest request, CancellationToken cancellationToken) =>
        WriteAsync(async () =>
        {
            var code = request?.RoleCode?.Trim();
            var name = request?.RoleName?.Trim();
            if (string.IsNullOrWhiteSpace(code) || !Regex.IsMatch(code, "^[A-Z][A-Z0-9_]{2,63}$")
                || code == SystemRoleCodes.SystemAdmin)
                return Validation<ManagedRoleDto>("普通角色编码必须是 3—64 位大写字母、数字或下划线，且不能使用内置编码。", "RoleCode");
            if (string.IsNullOrWhiteSpace(name)) return Validation<ManagedRoleDto>("角色名称不能为空。", "RoleName");
            var roleId = await repository.CreateRoleAsync(code, name, request?.Description?.Trim(), cancellationToken);
            var created = (await repository.GetRoleAsync(roleId, cancellationToken))!;
            await AuditAsync("CreateRole", "创建普通角色", roleId, null, created, cancellationToken);
            return ServiceResult<ManagedRoleDto>.Ok(created);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<ManagedRoleDto>> UpdateRoleAsync(Guid roleId, UpdateManagedRoleRequest request, CancellationToken cancellationToken) =>
        WriteAsync(async () =>
        {
            if (roleId == Guid.Empty || request is null || request.ExpectedVersion < 1)
                return Validation<ManagedRoleDto>("角色和预期版本无效。", "ExpectedVersion");
            var role = await repository.GetRoleForUpdateAsync(roleId, cancellationToken);
            if (role is null) return Blocked<ManagedRoleDto>("角色不存在。");
            if (role.IsSystem) return Forbidden<ManagedRoleDto>("内置角色不能编辑。");
            if (role.Version != request.ExpectedVersion) return Conflict<ManagedRoleDto>();
            var name = request.RoleName?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return Validation<ManagedRoleDto>("角色名称不能为空。", "RoleName");
            if (!await repository.UpdateRoleAsync(roleId, request.ExpectedVersion, name, request.Description?.Trim(), null, false, cancellationToken))
                return Conflict<ManagedRoleDto>();
            var updated = (await repository.GetRoleAsync(roleId, cancellationToken))!;
            await AuditAsync("UpdateRole", "修改角色资料", roleId, role, updated, cancellationToken);
            return ServiceResult<ManagedRoleDto>.Ok(updated);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<ManagedRoleDto>> SetRoleStatusAsync(Guid roleId, SetManagedStatusRequest request, CancellationToken cancellationToken) =>
        WriteAsync(async () =>
        {
            if (roleId == Guid.Empty || request is null || request.ExpectedVersion < 1)
                return Validation<ManagedRoleDto>("角色和预期版本无效。", "ExpectedVersion");
            var role = await repository.GetRoleForUpdateAsync(roleId, cancellationToken);
            if (role is null) return Blocked<ManagedRoleDto>("角色不存在。");
            if (role.IsSystem) return Forbidden<ManagedRoleDto>("内置角色不能停用或修改状态。");
            if (role.Version != request.ExpectedVersion) return Conflict<ManagedRoleDto>();
            if (!await repository.UpdateRoleAsync(roleId, request.ExpectedVersion, null, null, request.IsActive, false, cancellationToken))
                return Conflict<ManagedRoleDto>();
            var updated = (await repository.GetRoleAsync(roleId, cancellationToken))!;
            await AuditAsync("SetRoleStatus", "修改角色状态", roleId, role, updated, cancellationToken);
            return ServiceResult<ManagedRoleDto>.Ok(updated);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<bool>> DeleteRoleAsync(Guid roleId, int expectedVersion, CancellationToken cancellationToken) =>
        WriteAsync(async () =>
        {
            if (roleId == Guid.Empty || expectedVersion < 1) return Validation<bool>("角色和预期版本无效。", "ExpectedVersion");
            var role = await repository.GetRoleForUpdateAsync(roleId, cancellationToken);
            if (role is null) return Blocked<bool>("角色不存在。");
            if (role.IsSystem) return Forbidden<bool>("内置角色不能删除。");
            if (role.Version != expectedVersion) return Conflict<bool>();
            if (await repository.CountRoleUsersAsync(roleId, cancellationToken) > 0)
                return Blocked<bool>("角色仍关联用户，请先解除关系。");
            if (!await repository.UpdateRoleAsync(roleId, expectedVersion, null, null, null, true, cancellationToken))
                return Conflict<bool>();
            await AuditAsync("DeleteRole", "删除普通角色", roleId, role, new { IsDeleted = true }, cancellationToken);
            return ServiceResult<bool>.Ok(true);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<RolePermissionsDto>> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) =>
        ReadAsync("System.Roles.View", async () =>
        {
            var role = await repository.GetRoleAsync(roleId, cancellationToken);
            if (role is null) return Blocked<RolePermissionsDto>("角色不存在。");
            var automatic = role.IsSystem && role.RoleCode == SystemRoleCodes.SystemAdmin;
            var ids = automatic
                ? (await repository.GetPermissionsAsync(cancellationToken)).Where(item => item.IsActive).Select(item => item.Uid).ToList()
                : (await repository.GetRolePermissionIdsAsync(roleId, cancellationToken)).ToList();
            return ServiceResult<RolePermissionsDto>.Ok(new RolePermissionsDto
            { RoleId = roleId, Version = role.Version, PermissionIds = ids, IsAutomatic = automatic });
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<RolePermissionsDto>> ReplaceRolePermissionsAsync(Guid roleId, ReplaceRolePermissionsRequest request, CancellationToken cancellationToken) =>
        WriteAsync(async () =>
        {
            if (roleId == Guid.Empty || request?.PermissionIds is null || request.ExpectedVersion < 1)
                return Validation<RolePermissionsDto>("角色、权限集合或预期版本无效。", "PermissionIds");
            if (HasDuplicateOrEmpty(request.PermissionIds)) return Validation<RolePermissionsDto>("权限标识为空或重复。", "PermissionIds");
            var role = await repository.GetRoleForUpdateAsync(roleId, cancellationToken);
            if (role is null) return Blocked<RolePermissionsDto>("角色不存在。");
            if (role.IsSystem) return Forbidden<RolePermissionsDto>("内置管理员自动拥有全部权限，不能保存权限关系。");
            if (role.Version != request.ExpectedVersion) return Conflict<RolePermissionsDto>();
            var permissions = await repository.GetActivePermissionsAsync(request.PermissionIds, cancellationToken);
            if (permissions.Count != request.PermissionIds.Count || permissions.Any(item => !item.IsAssignable))
                return Validation<RolePermissionsDto>("权限不存在、已停用或不允许分配。", "PermissionIds");
            var before = await repository.GetRolePermissionIdsAsync(roleId, cancellationToken);
            await repository.ReplaceRolePermissionsAsync(roleId, request.ExpectedVersion, request.PermissionIds, cancellationToken);
            await AuditAsync("ReplaceRolePermissions", "角色权限整组替换", roleId, before, request.PermissionIds, cancellationToken);
            return ServiceResult<RolePermissionsDto>.Ok(new RolePermissionsDto
            { RoleId = roleId, Version = request.ExpectedVersion + 1, PermissionIds = request.PermissionIds.ToList() });
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<List<ManagedPermissionDto>>> GetPermissionsAsync(CancellationToken cancellationToken) =>
        ReadAsync("System.Permissions.View", async () =>
        {
            var items = await repository.GetPermissionsAsync(cancellationToken);
            if (items.Count != PermissionCatalog.All.Count || items.Any(item => PermissionCatalog.Find(item.PermissionKey) is null))
                return ServiceResult<List<ManagedPermissionDto>>.Fail(ServiceErrorCodes.BusinessBlocked, "数据库权限目录与代码不一致，请先执行显式同步。");
            return ServiceResult<List<ManagedPermissionDto>>.Ok(items.ToList());
        }, cancellationToken);

    /// <summary>只读请求仍实时查询管理员保留键，不能信任客户端隐藏入口。</summary>
    private async Task<ServiceResult<T>> ReadAsync<T>(string permissionKey, Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorId)) return Unauthenticated<T>();
        if (!await permissionEvaluation.HasPermissionAsync(actorId, permissionKey, cancellationToken))
            return Forbidden<T>("当前账号没有管理权限。");
        return await action();
    }

    /// <summary>管理写命令先锁内置角色并重验操作者，再执行目标聚合版本与业务校验。</summary>
    private async Task<ServiceResult<T>> WriteAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorId)) return Unauthenticated<T>();
        if (context.Session.Transaction is not null) return Blocked<T>("当前请求已有事务，不能接管授权管理写入。");
        using var transaction = context.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
        context.Session.Transaction = transaction;
        try
        {
            if (!await repository.LockAndCheckAdministratorAsync(actorId, cancellationToken))
            {
                transaction.Rollback();
                return Forbidden<T>("当前账号已不具备内置管理员身份。");
            }
            var result = await action();
            if (result.Success) transaction.Commit();
            else transaction.Rollback();
            return result;
        }
        catch (Exception exception) when (context.Session.Errors.Classify(exception) == DatabaseFailureKind.UniqueConflict)
        {
            RollbackSafely(transaction);
            return Validation<T>("稳定编码已存在或授权关系重复。", "Code");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RollbackSafely(transaction);
            throw;
        }
        catch (Exception exception)
        {
            RollbackSafely(transaction);
            context.LoggerFactory.CreateLogger<RbacManagementService>()
                .LogError(exception, "管理员 {ActorUserId} 的权限管理命令失败并已回滚。", actorId);
            return ServiceResult<T>.Fail(ServiceErrorCodes.Unexpected, "权限管理命令失败，未提交变更。");
        }
        finally
        {
            context.Session.Transaction = null;
        }
    }

    /// <summary>只接受实际登录身份，服务端不从请求体读取操作者。</summary>
    private bool TryGetActor(out Guid actorId)
    {
        actorId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && actorId != Guid.Empty;
    }

    /// <summary>在管理事务内追加授权关系前后集合，日志失败时回滚关系变更。</summary>
    private async Task AuditAsync(string operation, string name, Guid targetId,
        object? before, object? after, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var changed = JsonSerializer.Serialize(new { Before = before, After = after });
        if (await auditLogs.InsertAsync(new BusinessOperationLog
        {
            CommandId = Guid.NewGuid(), CorrelationId = Guid.NewGuid().ToString("N"),
            Module = "SystemRbac", AggregateType = operation.Contains("User", StringComparison.Ordinal) ? "SysUser" : "SysRole",
            AggregateUid = targetId, OperationType = operation, OperationName = name,
            Summary = "管理员调整授权关系。", ChangedFieldsJson = changed,
            UserUid = currentUser.UserId, UserNameSnapshot = currentUser.UserName,
            OccurredDate = DateTime.Now
        }) != 1)
            throw new InvalidOperationException("授权变更审计写入失败，事务必须回滚。");
    }

    private static bool ValidPage(ManagedPageRequest? request) => request is not null && request.PageIndex > 0 && request.PageSize is > 0 and <= 200;
    private static bool HasDuplicateOrEmpty(IReadOnlyCollection<Guid> ids) => ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Count;
    private static ServiceResult<T> FromNullable<T>(T? value, string message) where T : class => value is null ? Blocked<T>(message) : ServiceResult<T>.Ok(value);
    private static ServiceResult<T> Unauthenticated<T>() => ServiceResult<T>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");
    private static ServiceResult<T> Forbidden<T>(string message) => ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, message);
    private static ServiceResult<T> Blocked<T>(string message) => ServiceResult<T>.Fail(ServiceErrorCodes.BusinessBlocked, message);
    private static ServiceResult<T> Conflict<T>() => ServiceResult<T>.Fail(ServiceErrorCodes.VersionConflict, "数据版本已变化，请刷新后再试。");
    private static ServiceResult<T> Validation<T>(string message, string field) => ServiceResult<T>.Fail(ServiceErrorCodes.Validation, message, field);

    /// <summary>回滚异常不掩盖原始业务或数据库错误。</summary>
    private static void RollbackSafely(IDbTransaction transaction)
    {
        try { if (transaction.Connection is not null) transaction.Rollback(); }
        catch { /* 连接释放时仍会清理失败事务。 */ }
    }
}
