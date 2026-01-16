using SwiftExport.Application;      // 引用 Application 的扩展方法
using SwiftExport.Infrastructure;   // 引用 Infrastructure 的扩展方法

var builder = WebApplication.CreateBuilder(args);


// 1. 注册基础设施 
builder.Services.AddInfrastructureServices();

// 2. 注册应用逻辑
builder.Services.AddApplicationServices();

// ==========================================

// 3. 添加 Web 服务
#region 添加 Web 服务
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ... 中间件配置 (保持不变) ...

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