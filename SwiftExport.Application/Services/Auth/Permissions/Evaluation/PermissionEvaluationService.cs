using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Interfaces.Auth.Permissions;

namespace SwiftExport.Application.Services.Auth.Permissions.Evaluation;

/// <summary>由有效角色和代码目录生成唯一权限投影，不从显示名或旧模块关系授予权限。</summary>
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

        var roles = facts.RoleNames.Distinct(StringComparer.Ordinal).ToArray();
        var roleCodes = facts.RoleCodes.Distinct(StringComparer.Ordinal).ToArray();
        if (facts.IsAdmin)
        {
            return new UserPermissionProjection
            {
                IsAdmin = true,
                Roles = roles,
                RoleCodes = roleCodes,
                Permissions = PermissionCatalog.All.Select(item => item.PermissionKey).ToArray()
            };
        }

        var permissions = new HashSet<string>(facts.PermissionKeys, StringComparer.Ordinal);

        return new UserPermissionProjection
        {
            IsAdmin = false,
            Roles = roles,
            RoleCodes = roleCodes,
            Permissions = PermissionCatalog.All.Where(item => item.IsAssignable && permissions.Contains(item.PermissionKey))
                .Select(item => item.PermissionKey).ToArray()
        };
    }

    /// <inheritdoc />
    public async Task<bool> HasPermissionAsync(
        Guid userId,
        string permissionKey,
        CancellationToken cancellationToken = default)
    {
        if (PermissionCatalog.Find(permissionKey) is null)
            return false;
        var projection = await GetProjectionAsync(userId, cancellationToken);
        return projection?.Permissions.Contains(permissionKey, StringComparer.Ordinal) == true;
    }
}
