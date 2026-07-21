namespace SwiftExportClient.Services.Caching;

/// <summary>
/// 表示一次前端基础资料缓存加载或刷新的结果。
/// </summary>
public sealed class CacheRefreshResult
{
    private CacheRefreshResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    /// <summary>
    /// 获取缓存是否已成功更新。
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// 获取可供调用方提示或记录的结果消息。
    /// </summary>
    public string Message { get; }

    internal static CacheRefreshResult Ok(string message = "缓存已更新。") => new(true, message);

    internal static CacheRefreshResult Fail(string message) => new(false, message);
}
