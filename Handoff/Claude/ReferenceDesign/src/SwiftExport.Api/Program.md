# Program.cs（组合根）

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService().UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

// 认证 + 授权
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...);
builder.Services.AddAuthorization(o =>
	o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()); // 默认必须登录
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

// MVC + 校验 + 错误
builder.Services.AddControllers(o => o.Filters.Add<ValidationFilter>());
builder.Services.AddValidatorsFromAssemblyContaining<CreateProductValidator>();  // FluentValidation
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddSwaggerGen();

// 各层
builder.Services.AddApplication();       // Services、Queries、CommandExecutor、Mapster
builder.Services.AddInfrastructure(builder.Configuration);  // DbSession、Repositories、NpgsqlDataSource
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();

var app = builder.Build();
app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

不出现：自写 CorrelationId 中间件、自写异常中间件、端点权限字典、防重复请求过滤器。

关联 ID（决策 #26）：`Activity.Current.TraceId`（无 Activity 时 `HttpContext.TraceIdentifier`），由 `UseSerilogRequestLogging` 记录，`GlobalExceptionHandler` 写入 ProblemDetails `traceId`；审计表现有 CorrelationId 列写入该值。客户端不发送自定义关联头。
