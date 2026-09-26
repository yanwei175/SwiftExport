using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Services.Files;

/// <summary>以当前真实活动账号投影保护文件容器：活动用户可读，仅 Admin 可写。</summary>
public sealed class FileAccessPolicy(
    ICurrentUser currentUser,
    IPermissionEvaluationService permissionEvaluation) : IFileAccessPolicy
{
    /// <inheritdoc />
    public async Task<ServiceResult<bool>> AuthorizeReadAsync(CancellationToken cancellationToken = default)
    {
        var projection = await GetCurrentProjectionAsync(cancellationToken).ConfigureAwait(false);
        return projection is null
            ? Unauthenticated()
            : ServiceResult<bool>.Ok(true);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> AuthorizeWriteAsync(CancellationToken cancellationToken = default)
    {
        var projection = await GetCurrentProjectionAsync(cancellationToken).ConfigureAwait(false);
        if (projection is null) return Unauthenticated();
        return projection.IsAdmin
            ? ServiceResult<bool>.Ok(true)
            : ServiceResult<bool>.Fail(ServiceErrorCodes.Forbidden, "仅 Admin 可以上传或覆盖文件。");
    }

    /// <summary>只信任当前活动账号的实时权限投影，不从可能过时的 JWT 角色直接放行。</summary>
    private async Task<UserPermissionProjection?> GetCurrentProjectionAsync(
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? Guid.Empty;
        if (!currentUser.IsAuthenticated || userId == Guid.Empty) return null;
        return await permissionEvaluation.GetProjectionAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    private static ServiceResult<bool> Unauthenticated() =>
        ServiceResult<bool>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效或账号已停用，请重新登录。");
}
