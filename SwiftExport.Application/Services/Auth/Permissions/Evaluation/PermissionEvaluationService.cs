using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Interfaces.Auth.Permissions;

namespace SwiftExport.Application.Services.Auth.Permissions.Evaluation;

/// <summary>生成唯一权限投影并判断稳定权限键，保留现有采购兼容映射。</summary>
public sealed class PermissionEvaluationService(
    IUserPermissionRepository repository) : IPermissionEvaluationService
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
            .Where(permission => permission is not null && PermissionKeys.UserAssignable.Contains(permission, StringComparer.Ordinal))
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
}
