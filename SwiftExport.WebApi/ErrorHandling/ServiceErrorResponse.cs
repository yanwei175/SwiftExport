using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.ErrorHandling;

/// <summary>为 MVC、认证和中间件创建同一失败包装，并复用 MVC 的 JSON 配置。</summary>
public static class ServiceErrorResponse
{
    /// <summary>创建失败响应，保证错误消息一致、Data 为 null，技术故障不泄露内部详情。</summary>
    public static ObjectResult Create(string? code, string? message, string? field = null)
    {
        var status = ServiceErrorHttpMapper.GetStatusCode(code);
        if (status == 500)
        {
            code = ServiceErrorCodes.Unexpected;
            message = "系统发生内部错误，请联系管理员；写入结果请使用原命令确认。";
            field = null;
        }
        return new ObjectResult(ServiceResult<object>.Fail(code!, message ?? "请求未完成。", field!))
        {
            StatusCode = status
        };
    }

    /// <summary>在 MVC 之外写入失败；响应开始后不覆盖已有正文或状态。</summary>
    public static Task WriteAsync(HttpContext context, string code, string message)
    {
        if (context.Response.HasStarted) return Task.CompletedTask;
        var result = Create(code, message);
        context.Response.StatusCode = result.StatusCode!.Value;
        context.Response.ContentType = "application/json; charset=utf-8";
        var settings = context.RequestServices.GetRequiredService<IOptions<MvcNewtonsoftJsonOptions>>().Value.SerializerSettings;
        return context.Response.WriteAsync(JsonConvert.SerializeObject(result.Value, settings), context.RequestAborted);
    }
}
