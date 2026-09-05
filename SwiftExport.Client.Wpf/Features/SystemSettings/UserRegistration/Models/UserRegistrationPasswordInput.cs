namespace SwiftExportClient.Features.SystemSettings.UserRegistration.Models;

/// <summary>仅在创建用户时从 PasswordBox 传入密码，ViewModel 不持有密码绑定属性。</summary>
public sealed class UserRegistrationPasswordInput
{
    /// <summary>创建一次性注册密码输入。</summary>
    public UserRegistrationPasswordInput(string password, string confirmPassword)
    {
        Password = password;
        ConfirmPassword = confirmPassword;
    }

    /// <summary>获取初始密码。</summary>
    public string Password { get; }
    /// <summary>获取初始密码确认值。</summary>
    public string ConfirmPassword { get; }

    /// <summary>禁止调试或日志意外展开密码值。</summary>
    public override string ToString() => nameof(UserRegistrationPasswordInput);
}
