using Serilog.Context;

namespace SwiftExport.WebApi.Middlewares;

/// <summary>统一请求关联标识，并将其写入响应头、TraceIdentifier 和 Serilog 上下文。</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";

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

    private static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= 64
           && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or ':');
}
