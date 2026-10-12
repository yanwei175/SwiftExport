using System.Windows;
using Mapster;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExportClient.Authentication;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Services.Caching;

/// <summary>按会话隔离系统参数，序号防止退出及保存后的旧读取回写；不写入磁盘或轮询。</summary>
public sealed class SystemParameterCacheService : ISystemParameterCacheService, IDisposable
{
    private readonly ISystemParameterApi _api;
    private readonly IApiCallRunner _runner;
    private readonly ISessionService _session;
    private readonly ICurrentUserService _user;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SystemParameterDto[] _parameters = [];
    private Task<CacheRefreshResult>? _ensureTask;
    private string? _userId;
    private long _revision;
    private bool _loaded, _disposed;

    /// <summary>绑定会话清理及账号变化，权限刷新不会清除同账号快照。</summary>
    public SystemParameterCacheService(ISystemParameterApi api, IApiCallRunner runner, ISessionService session, ICurrentUserService user)
    {
        _api = api; _runner = runner; _session = session; _user = user;
        _userId = user.CurrentUser?.UserId;
        session.SessionEnded += OnSessionEnded;
        user.CurrentUserChanged += OnUserChanged;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;
    /// <inheritdoc />
    public IReadOnlyList<SystemParameterDto> Parameters { get { lock (_sync) return Array.AsReadOnly(_parameters.Select(Clone).ToArray()); } }
    /// <inheritdoc />
    public bool IsLoaded { get { lock (_sync) return _loaded; } }
    /// <inheritdoc />
    public bool TryGetValue(string key, out string value)
    {
        lock (_sync)
        {
            var item = _parameters.FirstOrDefault(parameter => parameter.Key == key && parameter.IsConfigured);
            value = item?.Value ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }
    }

    /// <inheritdoc />
    public Task<CacheRefreshResult> EnsureLoadedAsync()
    {
        lock (_sync)
        {
            if (_loaded) return Task.FromResult(CacheRefreshResult.Ok("系统参数已加载。"));
            if (_ensureTask is null || (_ensureTask.IsCompleted && (!_ensureTask.IsCompletedSuccessfully || !_ensureTask.Result.Success)))
                _ensureTask = EnsureCoreAsync(_revision);
            return _ensureTask;
        }
    }

    /// <summary>让出当前锁后才开始读取和通知，防止同步回执通知期间发生会话重入。</summary>
    private async Task<CacheRefreshResult> EnsureCoreAsync(long revision)
    {
        await Task.Yield();
        lock (_sync) if (_disposed || revision != _revision) return CacheRefreshResult.Fail("会话或参数已更新，本次初始化已失效。");
        return await RefreshAsync();
    }

    /// <inheritdoc />
    public async Task<CacheRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        long revision;
        lock (_sync)
        {
            if (_disposed || !_user.IsAuthenticated) return CacheRefreshResult.Fail("请先登录。");
            revision = _revision;
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            lock (_sync) if (revision != _revision || _disposed) return CacheRefreshResult.Fail("会话或参数已更新，本次读取已取消接纳。");
            var result = await _runner.RunAsync(() => _api.ListAsync(cancellationToken), cancellationToken, "读取系统参数失败");
            if (!result.Success || result.Data is null) return CacheRefreshResult.Fail(result.Message);
            lock (_sync)
            {
                if (revision != _revision || _disposed || !_user.IsAuthenticated)
                    return CacheRefreshResult.Fail("会话或参数已更新，本次读取已取消接纳。");
                _parameters = result.Data.Select(Clone).ToArray();
                _loaded = true;
            }
            NotifyChanged();
            return CacheRefreshResult.Ok("系统参数已从服务器刷新。已有产品地址保持原值。");
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public void ApplySaved(SystemParameterDto parameter)
    {
        lock (_sync)
        {
            if (_disposed || !_user.IsAuthenticated) return;
            ++_revision;
            _parameters = _parameters.Where(item => item.Key != parameter.Key).Append(Clone(parameter)).ToArray();
            _loaded = true;
            _ensureTask = null;
        }
        NotifyChanged();
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_sync) { ++_revision; _loaded = false; _parameters = []; _ensureTask = null; }
        NotifyChanged();
    }

    /// <summary>保护内部值，消费者修改 DTO 不会污染缓存基线。</summary>
    private static SystemParameterDto Clone(SystemParameterDto item) => item.Adapt<SystemParameterDto>();

    /// <summary>在 UI 线程通知图片控件和命令，不在缓存锁内调用外部订阅者。</summary>
    private void NotifyChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(() => Changed?.Invoke(this, EventArgs.Empty));
        else Changed?.Invoke(this, EventArgs.Empty);
    }
    private void OnSessionEnded(object? sender, SessionEndedEventArgs e) => Clear();
    /// <summary>权限刷新保留同账号快照，账号切换立即清空旧参数。</summary>
    private void OnUserChanged(object? sender, EventArgs e)
    {
        var id = _user.CurrentUser?.UserId;
        if (id == _userId) return;
        _userId = id;
        Clear();
    }

    /// <summary>解除进程级事件绑定；在途读取通过序号失效。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.SessionEnded -= OnSessionEnded;
        _user.CurrentUserChanged -= OnUserChanged;
        Clear();
    }
}
