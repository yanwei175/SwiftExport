using SwiftExport.Application.Interfaces.Auth;

namespace SwiftExport.Application.Services.Auth;

/// <summary>使用 BCrypt 生成并验证密码哈希。</summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    /// <inheritdoc />
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    /// <inheritdoc />
    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // 数据中若存在损坏或非 BCrypt 哈希，应按认证失败处理，不能泄露格式细节。
            return false;
        }
        catch (ArgumentException)
        {
            // 数据中若存在损坏或非 BCrypt 哈希，应按认证失败处理，不能泄露格式细节。
            return false;
        }
    }
}
