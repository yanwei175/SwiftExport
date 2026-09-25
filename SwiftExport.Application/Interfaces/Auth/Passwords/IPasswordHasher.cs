namespace SwiftExport.Application.Interfaces.Auth.Passwords;

/// <summary>隔离密码哈希实现，供认证服务和本机恢复工具复用。</summary>
public interface IPasswordHasher
{
    /// <summary>生成不可逆密码哈希。</summary>
    string Hash(string password);

    /// <summary>以恒定实现入口验证密码与已有哈希。</summary>
    bool Verify(string password, string passwordHash);
}
