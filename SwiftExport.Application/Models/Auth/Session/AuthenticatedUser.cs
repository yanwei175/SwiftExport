namespace SwiftExport.Application.Models.Auth.Session;

/// <summary>表示已通过密码验证、可用于签发 JWT 的最小身份。</summary>
public sealed class AuthenticatedUser
{
    /// <summary>用户稳定标识。</summary>
    public Guid UserId { get; init; }

    /// <summary>规范化后的登录用户名。</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>签发时写入 JWT 的当前凭据版本。</summary>
    public int CredentialVersion { get; init; }
}
