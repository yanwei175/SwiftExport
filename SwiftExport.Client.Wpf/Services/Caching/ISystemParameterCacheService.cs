using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;

namespace SwiftExportClient.Services.Caching;

/// <summary>当前登录会话的系统参数内存快照，不依赖其他基础资料缓存。</summary>
public interface ISystemParameterCacheService
{
    /// <summary>有效快照保存、刷新或会话清除后通知使用方。</summary>
    event EventHandler? Changed;
    /// <summary>获取隔离于内部缓存的只读参数快照。</summary>
    IReadOnlyList<SystemParameterDto> Parameters { get; }
    /// <summary>当前会话是否已有成功读取的快照。</summary>
    bool IsLoaded { get; }
    /// <summary>获取已配置值；缺失或未配置返回 false。</summary>
    bool TryGetValue(string key, out string value);
    /// <summary>去重首次加载，失败后允许重试。</summary>
    Task<CacheRefreshResult> EnsureLoadedAsync();
    /// <summary>手动刷新，失败保留最后一次成功快照。</summary>
    Task<CacheRefreshResult> RefreshAsync(CancellationToken cancellationToken = default);
    /// <summary>立即接纳保存回执，同时阻止较早的刷新覆盖新值。</summary>
    void ApplySaved(SystemParameterDto parameter);
    /// <summary>会话结束时清空快照并使在途请求失效。</summary>
    void Clear();
}
