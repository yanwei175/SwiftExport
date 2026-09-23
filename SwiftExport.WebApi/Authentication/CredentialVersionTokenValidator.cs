using SwiftExport.Application.Interfaces.Auth;
using System.Security.Claims;

namespace SwiftExport.WebApi.Authentication;

/// <summary>在 JWT 密码学验证后核对账号有效性和当前凭据版本。</summary>
public sealed class CredentialVersionTokenValidator(IAuthService authService)
{
    /// <summary>缺少身份或版本声明、账号失效、密码已改变时返回 false。</summary>
    public async Task<bool> ValidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        var versionValue = principal.FindFirstValue(AuthTokenClaims.CredentialVersion);
        return Guid.TryParse(userIdValue, out var userId)
            && int.TryParse(versionValue, out var credentialVersion)
            && credentialVersion > 0
            && await authService.IsCredentialVersionValidAsync(userId, credentialVersion, cancellationToken);
    }
}
