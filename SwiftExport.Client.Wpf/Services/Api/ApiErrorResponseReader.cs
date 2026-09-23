using System.Net;
using System.Text.Json;
using Refit;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExportClient.Services.Api;

/// <summary>读取非 2xx 的结构化失败；不依赖返回数据类型，不把网络故障当成业务拒绝。</summary>
public static class ApiErrorResponseReader
{
    /// <summary>保留服务端代码、提示与字段；缺失或损坏的错误正文按 HTTP 状态提供安全提示。</summary>
    public static ServiceResult<T> Read<T>(ApiException exception, string failureMessage)
    {
        if (!string.IsNullOrWhiteSpace(exception.Content))
        {
            try
            {
                using var document = JsonDocument.Parse(exception.Content);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False
                    && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(code.GetString())
                    && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                {
                    var field = error.TryGetProperty("field", out var path) && path.ValueKind == JsonValueKind.String ? path.GetString() : null;
                    return ServiceResult<T>.Fail(code.GetString()!, message.GetString()!, field!);
                }
            }
            catch (JsonException) { /* 网关 HTML、截断或错误 JSON 不进入用户提示。 */ }
        }

        var (fallbackCode, fallbackMessage) = exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized => (ServiceErrorCodes.Unauthenticated, "登录状态无效或已过期。"),
            HttpStatusCode.Forbidden => (ServiceErrorCodes.Forbidden, "当前账号没有执行该操作的权限。"),
            HttpStatusCode.NotFound => (ServiceErrorCodes.NotFound, "请求的接口或资源不存在。"),
            HttpStatusCode.BadRequest => (ServiceErrorCodes.Validation, "请求参数不正确。"),
            HttpStatusCode.Conflict => (ServiceErrorCodes.StateBlocked, "请求与当前数据状态冲突，请刷新确认。"),
            HttpStatusCode.UnprocessableEntity => (ServiceErrorCodes.BusinessBlocked, "当前业务条件不允许执行该操作。"),
            HttpStatusCode.TooManyRequests => (ServiceErrorCodes.RateLimited, "操作过于频繁，请稍后再试。"),
            _ => (ServiceErrorCodes.OutcomeUnknown, $"{failureMessage}：响应无法确认；写入操作请保留原命令重试。")
        };
        return ServiceResult<T>.Fail(fallbackCode, fallbackMessage);
    }
}
