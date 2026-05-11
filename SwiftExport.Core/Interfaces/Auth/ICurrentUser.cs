namespace SwiftExport.Core.Interfaces.Auth;

/// <summary>
/// 当前登录用户抽象，由 HTTP Context Claims 实现，供全局使用。
/// </summary>
public interface ICurrentUser
{
    /// <summary>用户名，未认证时返回 "system"</summary>
    string UserName { get; }

    /// <summary>用户 ID，未认证时返回 null</summary>
    int? UserId { get; }

    /// <summary>是否已认证（JWT 或 Dev 用户均算认证）</summary>
    bool IsAuthenticated { get; }
}
