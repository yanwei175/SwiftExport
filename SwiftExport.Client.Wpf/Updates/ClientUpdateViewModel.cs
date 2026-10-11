using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace SwiftExportClient.Updates;

/// <summary>拥有应用级更新状态；统一自动/手动入口，失败及窗口关闭时保留已发现更新。</summary>
public sealed partial class ClientUpdateViewModel(
    IClientUpdateService updates,
    IClientRestartGuard restartGuard,
    ILogger<ClientUpdateViewModel> logger) : ObservableObject, IDisposable
{
    private CancellationTokenSource? _operation;
    private bool _disposed;
    public string CurrentVersion => updates.CurrentVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(DownloadCommand), nameof(RestartCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyPropertyChangedFor(nameof(EntryLabel))]
    public partial ClientUpdateRelease? AvailableRelease { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(RestartCommand))]
    [NotifyPropertyChangedFor(nameof(EntryLabel))]
    public partial bool IsReadyToRestart { get; private set; }

    /// <summary>在 Shell 中持续显示可用更新，不强制弹窗打断业务操作。</summary>
    public string EntryLabel => IsReadyToRestart ? "更新已下载" : AvailableRelease is null ? "客户端更新" : "发现新版本";

    [ObservableProperty]
    public partial bool IsRestarting { get; private set; }

    [ObservableProperty]
    public partial int DownloadProgress { get; private set; }

    [ObservableProperty]
    public partial string Message { get; private set; } = "启动时自动检查，也可以手动检查更新。";

    /// <summary>从启动入口检查一次；与手动命令共用互斥和异常反馈。</summary>
    public Task CheckOnStartupAsync() => CheckAsync();

    private bool CanCheck() => !_disposed && !IsBusy;
    private bool CanDownload() => CanCheck() && AvailableRelease is not null && !IsReadyToRestart;
    private bool CanRestart() => CanCheck() && IsReadyToRestart;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        if (!CanCheck()) return;
        if (!updates.IsConfigured) { Message = "未配置有效的 HTTPS 更新地址，请联系管理员。"; return; }
        if (!updates.IsInstalled) { Message = "当前运行的是开发文件，请先使用 Setup 安装包安装客户端。"; return; }
        await RunAsync(async token =>
        {
            Message = "正在检查更新…";
            var release = await updates.CheckAsync(token);
            if (!IsCurrent(token)) return;
            AvailableRelease = release;
            IsReadyToRestart = updates.IsDownloaded;
            Message = release is null ? "当前已是最新版本。"
                : IsReadyToRestart ? $"版本 {release.Version} 已下载，请重启更新。"
                : $"发现新版本 {release.Version}，可以下载更新。";
        }, "更新检查失败，请检查网络或联系管理员后重试。");
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        if (!CanDownload()) return;
        await RunAsync(async token =>
        {
            Message = "正在下载并校验更新…";
            DownloadProgress = 0;
            var progress = new Progress<int>(value =>
            {
                if (IsCurrent(token)) DownloadProgress = Math.Clamp(value, 0, 100);
            });
            await updates.DownloadAsync(progress, token);
            if (!IsCurrent(token)) return;
            DownloadProgress = 100;
            IsReadyToRestart = true;
            Message = "下载完成，请重启更新；重启前会检查未保存编辑。";
        }, "下载更新失败，已保留更新信息，可以重试下载。");
    }

    [RelayCommand(CanExecute = nameof(CanRestart))]
    private async Task RestartAsync()
    {
        if (!CanRestart()) return;
        await RunAsync(async token =>
        {
            IsRestarting = true;
            try
            {
                if (!await restartGuard.CanRestartAsync(token))
                {
                    Message = "已取消重启。请先完成页面操作并处理未保存编辑。";
                    return;
                }
                if (!IsCurrent(token)) return;
                Message = "正在重启并应用更新…";
                updates.ApplyAndRestart();
            }
            finally { IsRestarting = false; }
        }, "启动更新失败，已保留下载包，请稍后重试重启更新。");
    }

    // 一个外层操作独占 Busy 和 CTS，关闭后的延迟结果、进度及异常不再回写 UI。
    private async Task RunAsync(Func<CancellationToken, Task> action, string failureMessage)
    {
        var operation = new CancellationTokenSource();
        _operation = operation;
        IsBusy = true;
        try { await action(operation.Token); }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Client update operation failed");
            if (IsCurrent(operation.Token)) Message = failureMessage;
        }
        finally
        {
            if (ReferenceEquals(_operation, operation))
            {
                _operation = null;
                if (!_disposed) IsBusy = false;
            }
            operation.Dispose();
        }
    }

    private bool IsCurrent(CancellationToken token) => !_disposed && !token.IsCancellationRequested
        && _operation?.Token == token;

    /// <summary>关闭更新窗口仅取消检查或下载，保留已发现版本；重启门禁中不允许关闭。</summary>
    public void CancelCurrentOperation()
    {
        if (IsRestarting) return;
        _operation?.Cancel();
        if (IsBusy && !_disposed) Message = "已取消更新操作。";
    }

    /// <summary>应用退出时释放生命周期，禁止异步延续修改已释放状态。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _operation?.Cancel();
    }
}
