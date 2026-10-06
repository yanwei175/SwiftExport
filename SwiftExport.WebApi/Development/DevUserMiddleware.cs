using SwiftExport.Infrastructure.Repositories.Auth.Development;
using System.Security.Claims;

namespace SwiftExport.WebApi.Development;

/// <summary>
/// 开发环境免登录中间件：当 Auth:UseDevUser=true 且请求未携带 Bearer Token 时，
/// 自动从数据库查询 dev 用户并注入 HttpContext.User，使后续 ICurrentUser 能取到 "dev"。
/// 生产环境永远不激活（Program.cs 启动时已阻断）。
/// </summary>
public class DevUserMiddleware(RequestDelegate next, IConfiguration config, IWebHostEnvironment env)
{
    private readonly RequestDelegate _next = next;
    private readonly IConfiguration _config = config;
    private readonly IWebHostEnvironment _env = env;

    // 缓存 dev 用户 Uid，避免每次请求都查 DB
    private static Guid? _cachedDevUserId;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>仅在允许的开发请求中注入开发身份，再执行后续管道。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // 仅在 Development 且配置开启时激活
        if (_env.IsDevelopment()
            && _config.GetValue<bool>("Auth:UseDevUser")
            && !HasBearerToken(context)
            && context.User?.Identity?.IsAuthenticated != true)
        {
            var devUserName = _config["Auth:DevUserName"] ?? "dev";
            var devUserId = await GetDevUserIdAsync(context, devUserName);

            if (devUserId.HasValue)
            {
                // 构造与 JWT 相同结构的 ClaimsPrincipal，使 ICurrentUser 能正常解析
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, devUserId.Value.ToString()),
                    new Claim(ClaimTypes.Name, devUserName),
                    new Claim("sub", devUserId.Value.ToString()),
                    new Claim("unique_name", devUserName),
                };
                var identity = new ClaimsIdentity(claims, "DevUser");
                context.User = new ClaimsPrincipal(identity);
            }
        }

        await _next(context);
    }

    /// <summary>请求 Header 是否包含 Bearer Token</summary>
    private static bool HasBearerToken(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        return authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>从 DB 查询 dev 用户 Uid（线程安全缓存）</summary>
    private static async Task<Guid?> GetDevUserIdAsync(HttpContext context, string devUserName)
    {
        if (_cachedDevUserId.HasValue)
            return _cachedDevUserId;

        await _semaphore.WaitAsync();
        try
        {
            if (_cachedDevUserId.HasValue)
                return _cachedDevUserId;

            // 从请求作用域获取 PG 查询，避免中间件构造阶段捕获 scoped 会话。
            var query = context.RequestServices.GetService<PostgreSqlDevUserQuery>();
            if (query == null) return null;

            var uid = await query.FindUidAsync(devUserName);
            _cachedDevUserId = uid;
            return uid;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
