using System.Data;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Assignment;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auth.Permissions;

namespace SwiftExport.Application.Services.Auth.Permissions.Assignment;

/// <summary>由真实 Admin 读取并原子替换用户的完整直接权限集合。</summary>
public sealed class UserPermissionAssignmentService(
    ServiceBaseContext context,
    IUserPermissionRepository repository,
    ICurrentUser currentUser,
    IPermissionEvaluationService permissionEvaluation) : IUserPermissionAssignmentService
{
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

        var unknown = request.ModulePermissions.FirstOrDefault(permission => !IsKnownAssignment(permission));
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
            context.LoggerFactory.CreateLogger<UserPermissionAssignmentService>()
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
        var projection = await permissionEvaluation.GetProjectionAsync(actorUserId, cancellationToken);
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
            ModulePermissions = (isAdmin ? PermissionKeys.UserAssignable : assignedModules.Where(IsKnownAssignment))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(permission => Array.IndexOf(PermissionKeys.UserAssignable.ToArray(), permission))
                .ToList()
        };

    /// <summary>限制普通用户权限维护入口只能写入公开可分配的稳定键。</summary>
    private static bool IsKnownAssignment(string? permission) =>
        permission is not null && PermissionKeys.UserAssignable.Contains(permission, StringComparer.Ordinal);

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
