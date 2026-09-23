using Serilog.Context;

namespace SwiftExport.WebApi.Observability;

/// <summary>统一请求关联标识，并将其写入响应头、TraceIdentifier 和 Serilog 上下文。</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>客户端与服务端传递调用关联标识的响应及请求头名称。</summary>
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>接纳合法关联标识或生成新标识，并在下游执行期间关联日志上下文。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var candidate = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = IsValid(candidate) ? candidate! : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    /// <summary>限制外部关联标识的长度和字符范围，避免任意内容进入日志。</summary>
    private static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= 64
           && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or ':');
}
