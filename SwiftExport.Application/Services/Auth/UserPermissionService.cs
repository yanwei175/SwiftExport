using System.Data;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Models.Auth;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Services.Auth;

/// <summary>集中合并模块、历史采购细权限和 Admin 固定权限，并编排模块整组替换。</summary>
public sealed class UserPermissionService(
    ServiceBaseContext context,
    IUserPermissionRepository repository,
    ICurrentUser currentUser) : IUserPermissionService
{
    /// <inheritdoc />
    public async Task<UserPermissionProjection?> GetProjectionAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return null;

        var facts = await repository.GetPermissionFactsAsync(userId, cancellationToken);
        if (facts is null || !facts.IsActive)
            return null;

        var roles = PermissionKeys.ExtractRoles(facts.RoleNames);
        if (facts.IsAdmin)
        {
            return new UserPermissionProjection
            {
                IsAdmin = true,
                Roles = roles,
                Permissions = PermissionKeys.All.ToArray()
            };
        }

        var modules = facts.ModulePermissions
            .Where(IsKnownModule)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var permissions = new HashSet<string>(modules, StringComparer.Ordinal);
        foreach (var legacyPermission in PermissionKeys.ProcurementOperations.Where(permission =>
                     facts.RoleNames.Any(role => string.Equals(role, permission, StringComparison.OrdinalIgnoreCase))))
        {
            permissions.Add(legacyPermission);
        }

        // 一级采购模块在唯一投影点展开，旧客户端和既有后端操作检查无需复制映射规则。
        if (permissions.Contains(PermissionKeys.ModuleProcurementAccess))
        {
            foreach (var operation in PermissionKeys.ProcurementOperations)
                permissions.Add(operation);
        }

        return new UserPermissionProjection
        {
            IsAdmin = false,
            Roles = roles,
            Permissions = PermissionKeys.All.Where(permissions.Contains).ToArray()
        };
    }

    /// <inheritdoc />
    public async Task<bool> HasPermissionAsync(
        Guid userId,
        string permissionKey,
        CancellationToken cancellationToken = default)
    {
        if (!PermissionKeys.All.Contains(permissionKey, StringComparer.Ordinal))
            return false;
        var projection = await GetProjectionAsync(userId, cancellationToken);
        return projection?.Permissions.Contains(permissionKey, StringComparer.Ordinal) == true;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<List<UserSummaryDto>>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var admin = await RequireAdminAsync(cancellationToken);
        if (!admin.Success)
            return ServiceResult<List<UserSummaryDto>>.Fail(admin.Error.Code, admin.Message);
        var users = await repository.GetActiveUsersAsync(cancellationToken);
        return ServiceResult<List<UserSummaryDto>>.Ok(users.ToList());
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserModulePermissionsDto>> GetUserModulesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var admin = await RequireAdminAsync(cancellationToken);
        if (!admin.Success)
            return ServiceResult<UserModulePermissionsDto>.Fail(admin.Error.Code, admin.Message);
        if (userId == Guid.Empty)
            return Validation<UserModulePermissionsDto>("必须选择目标用户。", "UserId");

        var facts = await repository.GetPermissionFactsAsync(userId, cancellationToken);
        if (facts is null || !facts.IsActive)
            return ServiceResult<UserModulePermissionsDto>.Fail(
                ServiceErrorCodes.BusinessBlocked, "目标用户不存在或已停用。", "UserId");

        return ServiceResult<UserModulePermissionsDto>.Ok(ToModulesDto(facts.UserId, facts.IsAdmin, facts.ModulePermissions));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserModulePermissionsDto>> SaveUserModulesAsync(
        Guid userId,
        SaveUserModulePermissionsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var admin = await RequireAdminAsync(cancellationToken);
        if (!admin.Success)
            return ServiceResult<UserModulePermissionsDto>.Fail(admin.Error.Code, admin.Message);
        if (userId == Guid.Empty)
            return Validation<UserModulePermissionsDto>("必须选择目标用户。", "UserId");
        if (request?.ModulePermissions is null)
            return Validation<UserModulePermissionsDto>("模块集合不能为空。", "ModulePermissions");

        var unknown = request.ModulePermissions.FirstOrDefault(permission => !IsKnownModule(permission));
        if (unknown is not null)
            return Validation<UserModulePermissionsDto>($"未知或不可分配的模块键：{unknown}", "ModulePermissions");
        var modules = request.ModulePermissions.Distinct(StringComparer.Ordinal).ToArray();

        if (!TryGetCurrentUserId(out var actorUserId))
            return Unauthenticated<UserModulePermissionsDto>();
        if (context.Session.Transaction is not null)
            return ServiceResult<UserModulePermissionsDto>.Fail(
                ServiceErrorCodes.BusinessBlocked, "模块权限保存不能接管已有事务。");

        using var transaction = context.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
        context.Session.Transaction = transaction;
        try
        {
            var status = await repository.ReplaceModulesAsync(actorUserId, userId, modules, cancellationToken);
            var failure = MapReplaceFailure(status);
            if (failure is not null)
            {
                transaction.Rollback();
                return failure;
            }

            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return ServiceResult<UserModulePermissionsDto>.Ok(
                ToModulesDto(userId, false, modules), "模块权限已保存，将在目标用户下次登录时更新界面。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RollbackSafely(transaction);
            throw;
        }
        catch (Exception exception)
        {
            RollbackSafely(transaction);
            context.LoggerFactory.CreateLogger<UserPermissionService>()
                .LogError(exception, "替换用户 {UserId} 的模块权限失败。", userId);
            return ServiceResult<UserModulePermissionsDto>.Fail(
                ServiceErrorCodes.Unexpected, "模块权限保存失败，未提交任何变更。");
        }
        finally
        {
            context.Session.Transaction = null;
        }
    }

    private async Task<ServiceResult<bool>> RequireAdminAsync(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
            return Unauthenticated<bool>();
        var projection = await GetProjectionAsync(actorUserId, cancellationToken);
        return projection?.IsAdmin == true
            ? ServiceResult<bool>.Ok(true)
            : ServiceResult<bool>.Fail(ServiceErrorCodes.Forbidden, "仅 Admin 可以维护用户模块权限。");
    }

    private static UserModulePermissionsDto ToModulesDto(
        Guid userId,
        bool isAdmin,
        IEnumerable<string> assignedModules) => new()
        {
            UserId = userId,
            IsAdmin = isAdmin,
            ModulePermissions = (isAdmin ? PermissionKeys.Modules : assignedModules.Where(IsKnownModule))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(permission => Array.IndexOf(PermissionKeys.Modules.ToArray(), permission))
                .ToList()
        };

    private static bool IsKnownModule(string? permission) =>
        permission is not null && PermissionKeys.Modules.Contains(permission, StringComparer.Ordinal);

    private static ServiceResult<UserModulePermissionsDto>? MapReplaceFailure(UserModuleReplaceStatus status) =>
        status switch
        {
            UserModuleReplaceStatus.Updated => null,
            UserModuleReplaceStatus.ActorNotAdmin => ServiceResult<UserModulePermissionsDto>.Fail(
                ServiceErrorCodes.Forbidden, "当前账号已不具备模块维护权限。"),
            UserModuleReplaceStatus.TargetIsAdmin => ServiceResult<UserModulePermissionsDto>.Fail(
                ServiceErrorCodes.Forbidden, "Admin 的固定全部权限不能通过模块接口修改。"),
            _ => ServiceResult<UserModulePermissionsDto>.Fail(
                ServiceErrorCodes.BusinessBlocked, "目标用户不存在或已停用。", "UserId")
        };

    private bool TryGetCurrentUserId(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && userId != Guid.Empty;
    }

    private static ServiceResult<T> Unauthenticated<T>() =>
        ServiceResult<T>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");

    private static ServiceResult<T> Validation<T>(string message, string field) =>
        ServiceResult<T>.Fail(ServiceErrorCodes.Validation, message, field);

    private static void RollbackSafely(IDbTransaction transaction)
    {
        try
        {
            if (transaction.Connection is not null)
                transaction.Rollback();
        }
        catch
        {
            // 原始异常或取消更能说明失败原因；释放事务时仍会完成最终清理。
        }
    }
}
