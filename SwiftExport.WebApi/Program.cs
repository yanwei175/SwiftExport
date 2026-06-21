using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using SwiftExport.Application;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure;
using SwiftExport.WebApi.Middlewares;
using SwiftExport.WebApi.Services;
using System.Text;



// 1. 读取配置文件
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .Build();
// ==========================================
// 初始化 Serilog (在构建系统前就启动，防闪退)
// ==========================================

// 2. 根据配合文件初始化 Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration) // ★ 这里的关键，读取 appsettings.json 的配 置
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} <s:{SourceContext}>{NewLine}{Exception}")
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();

//屏蔽底层啰嗦的日志，改成 Warning 级别以上才输出
//Log.Logger = new LoggerConfiguration()
//    .MinimumLevel.Information()
//    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning) // 屏蔽底层啰嗦的日志
//    .Enrich.FromLogContext()
//    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} <s:{SourceContext}>{NewLine}{Exception}")
//    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
//    .CreateLogger();

try
{
    Log.Information("SwiftExport 后端 Web API 启动中...");
    var builder = WebApplication.CreateBuilder(args);

    // ══════════════════════════════════════════════════════
    // 生产安全检查：禁止生产环境使用 DevUser（防裸奔）
    // ══════════════════════════════════════════════════════
    if (!builder.Environment.IsDevelopment()
        && builder.Configuration.GetValue<bool>("Auth:UseDevUser"))
    {
        throw new InvalidOperationException(
            "[安全阻断] 生产环境禁止开启 Auth:UseDevUser=true！请检查 appsettings.Production.json 配置。");
    }

    // ★ 告诉框架使用 Serilog 替代默认日志
    builder.Host.UseSerilog();

    // 1. 注册基础设施 
    builder.Services.AddInfrastructureServices();

    // 2. 注册应用逻辑
    builder.Services.AddApplicationServices();

    // ══════════════════════════════════════════════════════
    // 3. 注册认证与 ICurrentUser
    // ══════════════════════════════════════════════════════
    var jwtKey = builder.Configuration["Auth:JwtKey"]
        ?? throw new InvalidOperationException("Auth:JwtKey 未配置！");
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Auth:JwtIssuer"],
                ValidAudience = builder.Configuration["Auth:JwtAudience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
            };
        });

    // HttpContextAccessor 是 ICurrentUser 的依赖，必须注册
    builder.Services.AddHttpContextAccessor();
    // 注册 ICurrentUser（Scoped，每次请求从 HttpContext 读取）
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();

    // ══════════════════════════════════════════════════════
    // 4. 注册 Web API 核心服务
    // ══════════════════════════════════════════════════════
    #region 添加 Web 服务
    // 中文注释：注册内存缓存，供 PreventDuplicateRequestAttribute 防抖 Filter 使用。
    builder.Services.AddMemoryCache();
    // 中文注释：统一使用 Newtonsoft.Json，与前端 Refit（.NET Framework 4.8）行为完全一致。
    // 枚举字符串/整数双向支持、驼峰命名、null 忽略等均由 Newtonsoft 处理，无需额外 Converter。
    builder.Services.AddControllers()
        .AddNewtonsoftJson(options =>
        {
            // 枚举序列化为字符串名称（与前端 Refit 默认行为对齐）
            options.SerializerSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
            // null 值字段仍然输出，保持 API 响应字段完整（前端不需要做额外的 null 判断）
            options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Include;
        });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddRouting(options => options.LowercaseUrls = true);
    var app = builder.Build();

    // ... 中间件配置 (保持不变) ...
    // ==========================================
    // 4. 配置 HTTP 请求管道 (Middlewares)
    // ==========================================

    // ★ 挂载全局异常拦截器 (必须放在这里，也就是管道的最前面！)
    // 这样才能确保接住后面所有中间件和 Controller 抛出的异常。
    app.UseMiddleware<GlobalExceptionMiddleware>();

    // DevUser 免登录注入（仅 Development 且 UseDevUser=true 时激活）
    // 注意：必须在 UseAuthentication 之前，这样才能覆盖 HttpContext.User
    app.UseMiddleware<DevUserMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseAuthentication();   // ← 先认证（解析 JWT）
    app.UseAuthorization();    // ← 再授权
    app.MapControllers();

    app.Run();
    #endregion
}
catch (Exception ex)
{
    Log.Fatal(ex, "Web API 启动失败，遭遇致命异常！");
}
finally
{
    // 确保程序完全退出前，把内存里的日志全部刷入 txt 文件
    Log.CloseAndFlush();
}