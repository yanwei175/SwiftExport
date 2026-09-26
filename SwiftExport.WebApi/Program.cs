using Serilog;
using SwiftExport.WebApi.Configuration;

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
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
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

    builder.AddWebApiServices();
    var app = builder.Build();

    app.UseWebApiPipeline();

    app.Run();

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
