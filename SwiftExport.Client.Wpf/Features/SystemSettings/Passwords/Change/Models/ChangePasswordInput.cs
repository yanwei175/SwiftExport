namespace SwiftExportClient.Features.SystemSettings.Passwords.Change.Models;

/// <summary>仅在点击提交时从 PasswordBox 传入本人改密命令，不作为可绑定或缓存状态保存。</summary>
public sealed class ChangePasswordInput
{
    /// <summary>创建一次性本人改密输入。</summary>
    public ChangePasswordInput(string currentPassword, string newPassword, string confirmPassword)
    {
        CurrentPassword = currentPassword;
        NewPassword = newPassword;
        ConfirmPassword = confirmPassword;
    }

    /// <summary>获取当前密码。</summary>
    public string CurrentPassword { get; }
    /// <summary>获取新密码。</summary>
    public string NewPassword { get; }
    /// <summary>获取新密码确认值。</summary>
    public string ConfirmPassword { get; }

    /// <summary>禁止调试或日志意外展开密码值。</summary>
    public override string ToString() => nameof(ChangePasswordInput);
}
