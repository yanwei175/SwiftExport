using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using System.Text.Json;

namespace SwiftExport.WebApi.Filters
{
    /// <summary>
    /// 防抖式防重复提交过滤器。
    ///
    /// 使用方式（贴在写操作 Action 上）：
    ///   [PreventDuplicateRequest]                    // 默认 3 秒冷却窗口
    ///   [PreventDuplicateRequest(WindowSeconds = 5)] // 自定义冷却窗口
    ///
    /// 行为：
    ///   同一用户对同一接口，第一个请求进入后写入缓存标记；
    ///   在 WindowSeconds 冷却期内的后续请求直接返回 HTTP 429，不进入业务逻辑；
    ///   请求完成后不主动删除缓存 Key，保持完整的防抖冷却时间。
    ///
    /// 注意：
    ///   依赖 IMemoryCache（单机）；若多实例部署需替换为 IDistributedCache。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class PreventDuplicateRequestAttribute : Attribute, IAsyncActionFilter
    {
        /// <summary>
        /// 防抖冷却窗口（秒）。
        /// 第一个请求进入时开始计时，窗口期内的重复请求全部拦截。
        /// 默认 3 秒，适合保存/审批等写操作。
        /// </summary>
        public int WindowSeconds { get; set; } = 3;

        // 中文注释：静态只读，全局复用序列化配置，避免每次请求重复分配。
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var cache  = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var user   = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
            var logger = context.HttpContext.RequestServices
                             .GetRequiredService<ILogger<PreventDuplicateRequestAttribute>>();

            // ── 构建缓存 Key ──────────────────────────────────────────────────────
            // 格式：prevent_dup:{用户ID}:{Controller}:{Action}
            // UserId 可空：未认证时降级为 "anonymous"，依然能正确拦截匿名并发。
            // 不纳入请求体内容，避免哈希开销，按「用户 + 接口」粒度拦截即可。
            var userId     = user.UserId?.ToString() ?? "anonymous";
            var controller = context.ActionDescriptor.RouteValues["controller"] ?? "unknown";
            var action     = context.ActionDescriptor.RouteValues["action"]     ?? "unknown";
            var cacheKey   = $"prevent_dup:{userId}:{controller}:{action}";
            // ─────────────────────────────────────────────────────────────────────

            if (cache.TryGetValue(cacheKey, out _))
            {
                // 中文注释：命中缓存说明前一个相同请求处于冷却期，直接拒绝，不进入业务逻辑。
                logger.LogWarning(
                    "防重复请求拦截 | UserId={UserId} | Controller={Controller} | Action={Action} | WindowSeconds={Window}",
                    userId, controller, action, WindowSeconds);

                var result = ServiceResult<string>.Fail("操作过于频繁，请稍后再试。");

                context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
                context.HttpContext.Response.StatusCode  = StatusCodes.Status429TooManyRequests;

                await context.HttpContext.Response.WriteAsync(
                    JsonSerializer.Serialize(result, _jsonOptions));

                // 中文注释：设置空结果短路管道，阻止 Action 继续执行。
                context.Result = new EmptyResult();
                return;
            }

            // 中文注释：写入缓存标记，窗口期开始计时。
            // 绝对过期（AbsoluteExpirationRelativeToNow）保证冷却时间固定，
            // 不会因缓存滑动续期而意外延长。
            cache.Set(cacheKey, true, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(WindowSeconds),
                // 中文注释：Size 须与 AddMemoryCache(opt => opt.SizeLimit = N) 配合使用；
                // 若未设置 SizeLimit 则不需要此行，保持注释状态即可。
                // Size = 1
            });

            // 中文注释：防抖模式——finally 内不调用 cache.Remove，
            // 让 Key 在 WindowSeconds 内自然过期，提供完整冷却保护。
            await next();
        }
    }
}
