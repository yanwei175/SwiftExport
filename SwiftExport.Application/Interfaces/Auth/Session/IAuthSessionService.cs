using SwiftExport.Application.Models.Auth.Session;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Auth.Session;

/// <summary>定义登录、当前身份投影和 JWT 凭据版本核验入口。</summary>
public interface IAuthSessionService
{
    /// <summary>验证登录凭据并返回签发 Token 所需身份。</summary>
    Task<ServiceResult<AuthenticatedUser>> AuthenticateAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    /// <summary>读取当前活动账号及统一权限投影。</summary>
    Task<ServiceResult<MeDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default);
    /// <summary>核对 JWT 中的凭据版本与活动账号一致。</summary>
    Task<bool> IsCredentialVersionValidAsync(Guid userId, int credentialVersion, CancellationToken cancellationToken = default);
}
