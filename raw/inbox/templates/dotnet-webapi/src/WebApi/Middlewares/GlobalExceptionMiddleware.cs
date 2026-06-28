using System.Text.Json;
using {{ProjectName}}.Contracts.Wrappers;

namespace {{ProjectName}}.WebApi.Middlewares;

public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "发生未捕获异常。请求路径: {Path}", context.Request.Path);
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.StatusCode = StatusCodes.Status200OK;
            var json = JsonSerializer.Serialize(ServiceResult<string>.Fail("系统繁忙，请稍后再试。"), JsonOptions);
            await context.Response.WriteAsync(json);
        }
    }
}
