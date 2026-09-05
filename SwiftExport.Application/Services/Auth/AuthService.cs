using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Models.Auth;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Services.Auth;

/// <summary>编排认证、账号创建和密码维护，并保持密码与权限判断在服务端。</summary>
public sealed class AuthService(
    IAuthRepository repository,
    ICurrentUser currentUser,
    IPasswordHasher passwordHasher,
    IUserPermissionService userPermissionService) : IAuthService
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
            Permissions = projection.Permissions.ToList()
        });
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Guid>> RegisterAsync(
        RegisterRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentIdentity(out var actorUserId))
        {
            return Unauthenticated<Guid>();
        }

        if (!await repository.IsAdminAsync(actorUserId, cancellationToken))
        {
            return Forbidden<Guid>("仅 Admin 可以创建用户。");
        }

        if (request is null)
        {
            return Validation<Guid>("注册请求不能为空。", "Request");
        }

        var userName = request.UserName?.Trim();
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Validation<Guid>("用户名不能为空。", "UserName");
        }

        var passwordError = PasswordPolicy.Validate(request.Password, request.ConfirmPassword);
        if (passwordError is not null)
        {
            return Validation<Guid>(passwordError, "Password");
        }

        if (await repository.IsUserNameInUseAsync(userName, cancellationToken))
        {
            return Validation<Guid>($"用户名 {userName} 已存在。", "UserName");
        }

        var user = new SysUser
        {
            UserName = userName,
            NickName = string.IsNullOrWhiteSpace(request.NickName) ? userName : request.NickName.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            CredentialVersion = 1,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            IsActive = true
        };

        var createStatus = await repository.CreateUserByAdminAsync(actorUserId, user, cancellationToken);
        if (createStatus == AuthUserCreateStatus.ActorNotAdmin)
            return Forbidden<Guid>("当前账号已不具备创建用户的权限。");
        if (createStatus == AuthUserCreateStatus.UserNameInUse)
            return Validation<Guid>($"用户名 {userName} 已存在。", "UserName");

        return ServiceResult<Guid>.Ok(user.Uid, $"用户 {user.UserName} 创建成功，请继续分配模块权限。");
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> ChangeOwnPasswordAsync(
        ChangePasswordRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentIdentity(out var userId))
        {
            return Unauthenticated<bool>();
        }

        if (request is null)
        {
            return Validation<bool>("修改密码请求不能为空。", "Request");
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return Validation<bool>("旧密码不能为空。", "CurrentPassword");
        }

        var passwordError = PasswordPolicy.Validate(request.NewPassword, request.ConfirmPassword);
        if (passwordError is not null)
        {
            return Validation<bool>(passwordError, "NewPassword");
        }

        var user = await repository.GetActiveUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Unauthenticated<bool>();
        }

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return ServiceResult<bool>.Fail(
                ServiceErrorCodes.BusinessBlocked, "旧密码不正确。", "CurrentPassword");
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        if (!await repository.UpdateOwnPasswordAsync(user, cancellationToken))
        {
            return ServiceResult<bool>.Fail(
                ServiceErrorCodes.VersionConflict, "账号凭据已变化，请重新登录后再试。");
        }

        return ServiceResult<bool>.Ok(true, "密码已修改，请重新登录。");
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> ResetUserPasswordAsync(
        AdminResetPasswordRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentIdentity(out var actorUserId))
        {
            return Unauthenticated<bool>();
        }

        if (!await repository.IsAdminAsync(actorUserId, cancellationToken))
        {
            return Forbidden<bool>("仅 Admin 可以重置用户密码。");
        }

        if (request is null || request.UserId == Guid.Empty)
        {
            return Validation<bool>("必须选择要重置密码的用户。", "UserId");
        }

        var passwordError = PasswordPolicy.Validate(request.NewPassword, request.ConfirmPassword);
        if (passwordError is not null)
        {
            return Validation<bool>(passwordError, "NewPassword");
        }

        var target = await repository.GetActiveUserByIdAsync(request.UserId, cancellationToken);
        if (target is null)
        {
            return ServiceResult<bool>.Fail(ServiceErrorCodes.BusinessBlocked, "目标用户不存在或已停用。", "UserId");
        }

        if (await repository.IsAdminAsync(target.Uid, cancellationToken))
        {
            return Forbidden<bool>("管理员密码只能由本人修改或通过本机恢复工具处理。");
        }

        target.PasswordHash = passwordHasher.Hash(request.NewPassword);
        var status = await repository.ResetNonAdminPasswordAsync(actorUserId, target, cancellationToken);
        return status switch
        {
            AdminPasswordResetStatus.Updated => ServiceResult<bool>.Ok(true, "用户密码已重置。"),
            AdminPasswordResetStatus.ActorNotAdmin => Forbidden<bool>("当前账号已不具备重置密码的权限。"),
            AdminPasswordResetStatus.TargetIsAdmin => Forbidden<bool>("不能通过管理接口重置管理员密码。"),
            AdminPasswordResetStatus.TargetNotFound => ServiceResult<bool>.Fail(
                ServiceErrorCodes.BusinessBlocked, "目标用户不存在或已停用。", "UserId"),
            _ => ServiceResult<bool>.Fail(
                ServiceErrorCodes.VersionConflict, "目标账号凭据已变化，请刷新后再试。")
        };
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

    private static ServiceResult<T> Forbidden<T>(string message) =>
        ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, message);

    private static ServiceResult<T> Validation<T>(string message, string field) =>
        ServiceResult<T>.Fail(ServiceErrorCodes.Validation, message, field);
}
