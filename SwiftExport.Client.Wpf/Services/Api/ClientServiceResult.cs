namespace SwiftExportClient.Services.Api;

/// <summary>
/// 表示客户端已归一化的服务结果，使页面可用同一逻辑处理通用结果和兼容期采购结果。
/// </summary>
public sealed class ClientServiceResult<T>
{
    /// <summary>获取结果是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>获取成功数据。</summary>
    public T? Data { get; init; }

    /// <summary>获取用于页面反馈的消息。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>获取稳定错误代码。</summary>
    public string? ErrorCode { get; init; }

    /// <summary>获取相关字段路径。</summary>
    public string? ErrorField { get; init; }
}
