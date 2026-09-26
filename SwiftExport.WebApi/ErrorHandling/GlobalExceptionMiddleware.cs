using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Files;
using SwiftExport.Application.Commands.Validation;

namespace SwiftExport.WebApi.ErrorHandling;

/// <summary>统一处理请求取消、外层大小拒绝和未处理异常，不向客户端泄漏技术细节。</summary>
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
        catch (CommandValidationException exception)
        {
            if (context.Response.HasStarted) throw;
            var correlationId = context.TraceIdentifier;
            context.Response.Clear();
            context.Response.Headers["X-Correlation-ID"] = correlationId;
            await ServiceErrorResponse.WriteAsync(context, ServiceErrorCodes.Validation, exception.Message, exception.Field);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            if (context.Response.HasStarted) throw;
            context.Response.Clear();
            await ServiceErrorResponse.WriteAsync(
                context,
                FileErrorCodes.UploadTooLarge,
                "上传内容超过服务器允许的大小。",
                "Content-Length");
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
