namespace SwiftExportClient.Updates;

/// <summary>隔离安装状态、更新包检查、下载校验及进程重启。</summary>
public interface IClientUpdateService
{
    string CurrentVersion { get; }
    bool IsInstalled { get; }
    bool IsConfigured { get; }
    bool IsDownloaded { get; }
    /// <summary>读取可用更新或已下载待应用的更新，不自动下载。</summary>
    Task<ClientUpdateRelease?> CheckAsync(CancellationToken cancellationToken);
    /// <summary>下载并校验上一次检查的包；支持进度和取消。</summary>
    Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken);
    /// <summary>应用已校验的包并退出当前进程；调用前必须通过重启门禁。</summary>
    void ApplyAndRestart();
}
