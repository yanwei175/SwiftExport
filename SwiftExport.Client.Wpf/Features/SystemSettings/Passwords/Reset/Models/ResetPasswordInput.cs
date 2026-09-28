namespace SwiftExportClient.Features.SystemSettings.Passwords.Reset.Models;

/// <summary>仅在点击提交时从 PasswordBox 传入管理员重置命令，不长期保存密码。</summary>
public sealed class ResetPasswordInput
{
    /// <summary>创建一次性管理员密码重置输入。</summary>
    public ResetPasswordInput(string newPassword, string confirmPassword)
    {
        NewPassword = newPassword;
        ConfirmPassword = confirmPassword;
    }

    /// <summary>获取目标用户的新密码。</summary>
    public string NewPassword { get; }
    /// <summary>获取新密码确认值。</summary>
    public string ConfirmPassword { get; }

    /// <summary>禁止调试或日志意外展开密码值。</summary>
    public override string ToString() => nameof(ResetPasswordInput);
}
