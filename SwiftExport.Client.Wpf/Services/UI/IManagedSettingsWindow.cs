namespace SwiftExportClient.Services.UI;

/// <summary>允许设置窗口服务在会话结束时绕过交互确认并清理旧会话窗口。</summary>
internal interface IManagedSettingsWindow
{
    /// <summary>强制关闭窗口并释放页面级资源。</summary>
    void ForceClose();
}
