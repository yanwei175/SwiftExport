namespace SwiftExportClient.Services.UI;

/// <summary>统一打开设置中心的独立模态窗口，并保证同类窗口只有一个实例。</summary>
public interface ISettingsWindowService
{
    /// <summary>打开或激活本人密码修改窗口。</summary>
    void ShowChangePassword();

    /// <summary>打开或激活用户创建窗口。</summary>
    void ShowUserRegistration();

    /// <summary>打开或激活管理员密码重置窗口。</summary>
    void ShowResetPassword();

    /// <summary>打开或激活用户模块权限窗口。</summary>
    void ShowUserModules();

    /// <summary>打开或激活字典管理窗口。</summary>
    void ShowDictionaries();
}
