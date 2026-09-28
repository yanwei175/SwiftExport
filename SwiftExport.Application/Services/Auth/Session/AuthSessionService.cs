using SwiftExport.Application.Interfaces.Auth.Session;
using SwiftExport.Application.Interfaces.Auth.Passwords;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Session;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auth.Accounts;

namespace SwiftExport.Application.Services.Auth.Session;

/// <summary>验证登录、读取当前身份与权限投影，并核对 JWT 凭据版本。</summary>
public sealed class AuthSessionService(
    IAuthRepository repository,
    ICurrentUser currentUser,
    IPasswordHasher passwordHasher,
    IPermissionEvaluationService userPermissionService) : IAuthSessionService
{
    /// <inheritdoc />
    public async Task<ServiceResult<AuthenticatedUser>> AuthenticateAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Validation<AuthenticatedUser>("登录请求不能为空。", "Request");
        }

        var userName = request.UserName?.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(request?.Password))
        {
            return ServiceResult<AuthenticatedUser>.Fail(
                ServiceErrorCodes.Validation, "用户名和密码不能为空。", "UserName");
        }

        var user = await repository.GetActiveUserByNameAsync(userName, cancellationToken);
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return ServiceResult<AuthenticatedUser>.Fail(
                ServiceErrorCodes.Unauthenticated, "用户名或密码错误。");
        }

        return ServiceResult<AuthenticatedUser>.Ok(new AuthenticatedUser
        {
            UserId = user.Uid,
            UserName = user.UserName,
            CredentialVersion = user.CredentialVersion
        });
    }

    /// <inheritdoc />
    public async Task<ServiceResult<MeDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentIdentity(out var userId))
        {
            return Unauthenticated<MeDto>();
        }

        var user = await repository.GetActiveUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Unauthenticated<MeDto>();
        }

        var projection = await userPermissionService.GetProjectionAsync(userId, cancellationToken);
        if (projection is null)
        {
            return Unauthenticated<MeDto>();
        }

        return ServiceResult<MeDto>.Ok(new MeDto
        {
            UserId = user.Uid.ToString(),
            UserName = user.UserName,
            NickName = string.IsNullOrWhiteSpace(user.NickName) ? user.UserName : user.NickName,
            Roles = projection.Roles.ToList(),
            RoleCodes = projection.RoleCodes.ToList(),
            IsAdmin = projection.IsAdmin,
            Permissions = projection.Permissions.ToList()
        });
    }

    /// <inheritdoc />
    public Task<bool> IsCredentialVersionValidAsync(
        Guid userId,
        int credentialVersion,
        CancellationToken cancellationToken = default)
    {
        return credentialVersion > 0
            ? repository.IsCredentialVersionValidAsync(userId, credentialVersion, cancellationToken)
            : Task.FromResult(false);
    }

    private bool TryGetCurrentIdentity(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && userId != Guid.Empty;
    }

    private static ServiceResult<T> Unauthenticated<T>() =>
        ServiceResult<T>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效，请重新登录。");

    private static ServiceResult<T> Validation<T>(string message, string field) =>
        ServiceResult<T>.Fail(ServiceErrorCodes.Validation, message, field);
}
