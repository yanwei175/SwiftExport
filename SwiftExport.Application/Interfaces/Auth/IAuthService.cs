using SwiftExport.Application.Models.Auth;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Auth;

/// <summary>定义登录、当前用户、账号创建和密码维护的认证用例。</summary>
public interface IAuthService
{
    /// <summary>验证用户名和密码，成功时返回签发 Token 所需的最小身份。</summary>
    Task<ServiceResult<AuthenticatedUser>> AuthenticateAsync(LoginRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>读取当前活动用户、角色和兼容权限投影。</summary>
    Task<ServiceResult<MeDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>由当前 Admin 创建无角色、无模块权限的新用户。</summary>
    Task<ServiceResult<Guid>> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>验证当前用户旧密码并修改本人密码。</summary>
    Task<ServiceResult<bool>> ChangeOwnPasswordAsync(ChangePasswordRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>由当前 Admin 重置指定非 Admin 用户密码。</summary>
    Task<ServiceResult<bool>> ResetUserPasswordAsync(AdminResetPasswordRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>验证 JWT 中的凭据版本与当前活动账号一致。</summary>
    Task<bool> IsCredentialVersionValidAsync(Guid userId, int credentialVersion, CancellationToken cancellationToken = default);
}
