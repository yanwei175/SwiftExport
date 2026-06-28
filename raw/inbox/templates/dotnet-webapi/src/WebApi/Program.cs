using {{ProjectName}}.Application;
using {{ProjectName}}.Core.Interfaces.Auth;
using {{ProjectName}}.Infrastructure;
using {{ProjectName}}.WebApi.Middlewares;
using {{ProjectName}}.WebApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructureServices();
builder.Services.AddApplicationServices();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddMemoryCache();

builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
        options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Include;
    });

builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddRouting(options => options.LowercaseUrls = true);

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
// 中文注释：如需 JWT 或其他认证，请先注册 AddAuthentication，再启用 UseAuthentication。
// app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
