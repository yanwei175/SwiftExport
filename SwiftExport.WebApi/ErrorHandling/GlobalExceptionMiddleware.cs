using SwiftExport.Contracts.DTOs.Errors;

namespace SwiftExport.WebApi.ErrorHandling;

/// <summary>记录未处理异常，返回安全的 HTTP 500；客户端断开不伪装成业务失败。</summary>
public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    /// <summary>执行下游管道；已开始的响应不能改写，异常继续交给服务器终止连接。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("客户端已取消请求，Path={Path}", context.Request.Path);
            if (!context.Response.HasStarted) context.Response.StatusCode = 499;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "未处理的请求异常，Path={Path}", context.Request.Path);
            if (context.Response.HasStarted) throw;
            // 清除下游可能设置的长度和内容类型，但保留追踪标识。
            var correlationId = context.TraceIdentifier;
            context.Response.Clear();
            context.Response.Headers["X-Correlation-ID"] = correlationId;
            await ServiceErrorResponse.WriteAsync(context, ServiceErrorCodes.Unexpected, "系统发生内部错误。");
        }
    }
}