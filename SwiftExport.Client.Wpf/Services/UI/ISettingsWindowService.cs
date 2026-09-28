namespace SwiftExportClient.Services.UI;

/// <summary>打开个人改密和单实例系统设置窗口。</summary>
public interface ISettingsWindowService
{
    /// <summary>打开或激活本人密码修改窗口。</summary>
    void ShowChangePassword();

    /// <summary>按当前 me 权限打开或激活独立的系统设置窗口。</summary>
    void ShowSystemSettings();
}
