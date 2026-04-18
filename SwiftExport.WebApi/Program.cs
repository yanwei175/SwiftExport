using Serilog;
using Serilog.Events;
using SwiftExport.Application;
using SwiftExport.Infrastructure;
using SwiftExport.WebApi.Middlewares;



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

    // ★ 告诉框架使用 Serilog 替代默认日志
    builder.Host.UseSerilog();

    // 1. 注册基础设施 
    builder.Services.AddInfrastructureServices();

    // 2. 注册应用逻辑
    builder.Services.AddApplicationServices();

    // ==========================================
    // 3. 注册 Web API 核心服务
    // ==========================================
    #region 添加 Web 服务
    builder.Services.AddControllers();
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

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseAuthorization();
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