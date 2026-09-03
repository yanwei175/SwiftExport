using SwiftExport.Contracts.Wrappers;

namespace SwiftExportClient.Services.Api;

/// <summary>
/// 将不同服务结果包装归一为客户端结果，确保页面只维护一套消息和错误分类逻辑。
/// </summary>
public static class ServiceResultNormalizer
{
    /// <summary>归一化通用服务结果。</summary>
    public static ClientServiceResult<T> ToClientResult<T>(this ServiceResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Create(result.Success, result.Data, result.Error?.Message ?? result.Message,
            result.Error?.Code, result.Error?.Field);
    }

    /// <summary>集中建立页面结果，避免页面复制消息处理规则。</summary>
    private static ClientServiceResult<T> Create<T>(bool success, T? data, string? message, string? errorCode, string? errorField)
    {
        return new ClientServiceResult<T>
        {
            Success = success,
            Data = data,
            Message = message ?? string.Empty,
            ErrorCode = errorCode,
            ErrorField = errorField
        };
    }
}
