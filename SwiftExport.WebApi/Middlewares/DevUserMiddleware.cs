using Dapper;
using SwiftExport.Infrastructure.Data;
using System.Security.Claims;

namespace SwiftExport.WebApi.Middlewares;

/// <summary>
/// 开发环境免登录中间件：当 Auth:UseDevUser=true 且请求未携带 Bearer Token 时，
/// 自动从数据库查询 dev 用户并注入 HttpContext.User，使后续 ICurrentUser 能取到 "dev"。
/// 生产环境永远不激活（Program.cs 启动时已阻断）。
/// </summary>
public class DevUserMiddleware(
    RequestDelegate next,
    IConfiguration config,
    IWebHostEnvironment env)
{
    private readonly RequestDelegate _next = next;
    private readonly IConfiguration _config = config;
    private readonly IWebHostEnvironment _env = env;

    // 缓存 dev 用户 ID，避免每次请求都查 DB
    private static int? _cachedDevUserId;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

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

    /// <summary>从 DB 查询 dev 用户 ID（线程安全缓存）</summary>
    private static async Task<int?> GetDevUserIdAsync(HttpContext context, string devUserName)
    {
        if (_cachedDevUserId.HasValue)
            return _cachedDevUserId;

        await _semaphore.WaitAsync();
        try
        {
            if (_cachedDevUserId.HasValue)
                return _cachedDevUserId;

            // 从 DI 获取 DbSession（Scoped），不能从构造函数注入
            var dbSession = context.RequestServices.GetService<DbSession>();
            if (dbSession == null) return null;

            const string sql = """
                SELECT TOP 1 Id FROM [SysUser] WITH(NOLOCK)
                WHERE UserName = @UserName AND IsActive = 1 AND (IsDeleted = 0 OR IsDeleted IS NULL)
                """;
            var id = await dbSession.Connection.ExecuteScalarAsync<int?>(sql, new { UserName = devUserName });
            _cachedDevUserId = id;
            return id;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
