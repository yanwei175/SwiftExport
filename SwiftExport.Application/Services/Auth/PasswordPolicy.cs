namespace SwiftExport.Application.Services.Auth;

/// <summary>集中维护本阶段创建账号与改密使用的最小密码规则。</summary>
public static class PasswordPolicy
{
    /// <summary>当前所有密码创建与修改路径共用的最小长度。</summary>
    public const int MinimumLength = 6;

    /// <summary>校验密码非空、长度及二次确认；密码内容不做 Trim。</summary>
    public static string? Validate(string? password, string? confirmation)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return "密码不能为空。";
        }

        if (password.Length < MinimumLength)
        {
            return $"密码长度不能少于 {MinimumLength} 位。";
        }

        return password == confirmation ? null : "两次密码不一致。";
    }
}
