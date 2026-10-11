using System.Reflection;
using Microsoft.Extensions.Options;
using Velopack;

namespace SwiftExportClient.Updates;

/// <summary>使用独立静态 HTTPS 源更新客户端，保留已下载包供下次启动继续应用。</summary>
public sealed class VelopackUpdateService : IClientUpdateService
{
    private readonly UpdateManager? _manager;
    private UpdateInfo? _available;
    private VelopackAsset? _downloaded;

    /// <summary>只允许 HTTPS 更新源，不绕过系统证书验证，也不携带业务 Token。</summary>
    public VelopackUpdateService(IOptions<ClientUpdateOptions> options)
    {
        if (Uri.TryCreate(options.Value.FeedUrl, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
        {
            _manager = new UpdateManager(uri.AbsoluteUri.TrimEnd('/') + "/");
        }
    }

    public string CurrentVersion => _manager?.CurrentVersion?.ToString()
        ?? typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
    public bool IsInstalled => _manager?.IsInstalled == true;
    public bool IsConfigured => _manager is not null;
    public bool IsDownloaded => _downloaded is not null;

    /// <inheritdoc />
    public async Task<ClientUpdateRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        var manager = _manager ?? throw new InvalidOperationException("未配置更新源。");
        // 检查 API 没有取消参数：等待它结束后复核 Token，拒绝关闭后的结果回写。
        var pending = await Task.Run(() => manager.UpdatePendingRestart, cancellationToken);
        if (pending is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _downloaded = pending;
            return new(pending.Version.ToString(), pending.NotesMarkdown ?? string.Empty);
        }
        var available = await manager.CheckForUpdatesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        _available = available;
        return available is null ? null
            : new(available.TargetFullRelease.Version.ToString(), available.TargetFullRelease.NotesMarkdown ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        var available = _available ?? throw new InvalidOperationException("请先检查更新。");
        await _manager!.DownloadUpdatesAsync(available, progress.Report, cancellationToken);
        // Velopack 只有完成哈希/大小校验后才返回；取消或失败不会开放重启。
        cancellationToken.ThrowIfCancellationRequested();
        _downloaded = available.TargetFullRelease;
    }

    /// <inheritdoc />
    public void ApplyAndRestart()
    {
        var downloaded = _downloaded ?? throw new InvalidOperationException("更新尚未下载完成。");
        _manager!.ApplyUpdatesAndRestart(downloaded);
    }
}
