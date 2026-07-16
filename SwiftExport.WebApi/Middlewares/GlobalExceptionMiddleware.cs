using SwiftExport.Contracts.Wrappers;
using System.Text.Json;

namespace SwiftExport.WebApi.Middlewares
{
    public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        private readonly RequestDelegate _next = next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger = logger;

        // ★ 核心修复：定义为静态只读，全局复用这一个实例，性能拉满！
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                _logger.LogDebug("客户端已取消请求。请求路径: {Path}", context.Request.Path);
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = 499;
                }
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception ex)
        {
            _logger.LogError(ex, "发生了未捕获的全局系统异常。请求路径: {Path}", context.Request.Path);

            var response = ServiceResult<string>.Fail("系统繁忙或发生内部错误，请稍后再试或联系管理员。");

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = StatusCodes.Status200OK;

            // ★ 直接使用复用的静态配置进行序列化
            var json = JsonSerializer.Serialize(response, _jsonOptions);

            await context.Response.WriteAsync(json);
        }
    }
}
