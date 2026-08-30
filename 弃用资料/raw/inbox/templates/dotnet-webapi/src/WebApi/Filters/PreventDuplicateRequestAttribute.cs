using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using {{ProjectName}}.Contracts.Wrappers;
using {{ProjectName}}.Core.Interfaces.Auth;

namespace {{ProjectName}}.WebApi.Filters;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class PreventDuplicateRequestAttribute : Attribute, IAsyncActionFilter
{
    public int WindowSeconds { get; set; } = 3;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
        var user = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        var userId = user.UserId?.ToString() ?? "anonymous";
        var controller = context.ActionDescriptor.RouteValues["controller"] ?? "unknown";
        var action = context.ActionDescriptor.RouteValues["action"] ?? "unknown";
        var cacheKey = $"prevent_dup:{userId}:{controller}:{action}";

        if (cache.TryGetValue(cacheKey, out _))
        {
            context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            var json = JsonSerializer.Serialize(ServiceResult<string>.Fail("操作过于频繁，请稍后再试。"), JsonOptions);
            await context.HttpContext.Response.WriteAsync(json);
            context.Result = new EmptyResult();
            return;
        }

        cache.Set(cacheKey, true, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(WindowSeconds)
        });

        await next();
    }
}
