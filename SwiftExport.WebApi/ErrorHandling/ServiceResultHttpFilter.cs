using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.ErrorHandling;

/// <summary>统一处理 Action 和短路过滤器的服务失败；成功结果沿用端点自身的响应契约。</summary>
public sealed class ServiceResultHttpFilter(ILogger<ServiceResultHttpFilter> logger) : IAlwaysRunResultFilter
{
    /// <summary>在序列化前将业务失败映射为 HTTP 错误，避免返回 200 或值类型默认 Data。</summary>
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not ObjectResult { Value: IServiceResult { Success: false } result }) return;
        var response = ServiceErrorResponse.Create(result.Error?.Code, result.Error?.Message ?? result.Message, result.Error?.Field);
        if (response.StatusCode == 500)
            logger.LogError("服务错误进入技术故障出口，Code={Code}，Path={Path}", result.Error?.Code, context.HttpContext.Request.Path);
        if (response.StatusCode == 401 && !context.HttpContext.Response.Headers.ContainsKey("WWW-Authenticate"))
            context.HttpContext.Response.Headers.WWWAuthenticate = "Bearer";
        if (response.StatusCode == 429 && !context.HttpContext.Response.Headers.ContainsKey("Retry-After"))
            context.HttpContext.Response.Headers.RetryAfter = "1";
        context.Result = response;
    }

    /// <summary>结果执行完成后无需再次写入响应。</summary>
    public void OnResultExecuted(ResultExecutedContext context) { }
}
