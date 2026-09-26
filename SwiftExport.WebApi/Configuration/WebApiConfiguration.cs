using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Application;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Infrastructure;
using SwiftExport.WebApi.Development;
using SwiftExport.WebApi.ErrorHandling;
using SwiftExport.WebApi.Observability;

using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.Files;
using System.Text;

namespace SwiftExport.WebApi.Configuration;

/// <summary>集中组合 WebApi 的依赖、认证、序列化和请求管道，统一注册公共 HTTP 边界。</summary>
public static class WebApiConfiguration
{
    /// <summary>注册后端依赖、JWT 身份验证和 HTTP 框架选项；沿用宿主配置。</summary>
    public static void AddWebApiServices(this WebApplicationBuilder builder)
    {
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
                options.IncludeErrorDetails = false;
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        return ServiceErrorResponse.WriteAsync(context.HttpContext, ServiceErrorCodes.Unauthenticated, "登录状态无效或已过期。");
                    },
                    OnForbidden = context => ServiceErrorResponse.WriteAsync(context.HttpContext, ServiceErrorCodes.Forbidden, "当前账号没有执行该操作的权限。"),
                    OnTokenValidated = async context =>
                    {
                        var validator = context.HttpContext.RequestServices
                            .GetRequiredService<CredentialVersionTokenValidator>();
                        if (!await validator.ValidateAsync(context.Principal!, context.HttpContext.RequestAborted))
                        {
                            context.Fail("账号凭据已变化，请重新登录。");
                        }
                    }
                };
            });

        // HttpContextAccessor 是 ICurrentUser 的依赖，必须注册
        builder.Services.AddHttpContextAccessor();
        // 注册 ICurrentUser（Scoped，每次请求从 HttpContext 读取）
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
        builder.Services.AddSingleton<IAuthTokenFactory, AuthTokenFactory>();
        builder.Services.AddScoped<CredentialVersionTokenValidator>();

        // ══════════════════════════════════════════════════════
        // 4. 注册 Web API 核心服务
        // ══════════════════════════════════════════════════════

        // 中文注释：注册内存缓存，供 PreventDuplicateRequestAttribute 防抖 Filter 使用。
        builder.Services.AddMemoryCache();
        // 中文注释：统一使用 Newtonsoft.Json，与前端 Refit（.NET Framework 4.8）行为完全一致。
        // 枚举字符串/整数双向支持、驼峰命名、null 忽略等均由 Newtonsoft 处理，无需额外 Converter。
        builder.Services.AddControllers(options => options.Filters.Add<ServiceResultHttpFilter>())
            .ConfigureApiBehaviorOptions(options =>
            {
                options.SuppressMapClientErrors = true;
                options.InvalidModelStateResponseFactory = context =>
                {
                    var first = context.ModelState.FirstOrDefault(item => item.Value?.Errors.Count > 0);
                    // 不回传 JSON 解析器的异常文本或原始输入；字段路径用于定位表单。
                    return ServiceErrorResponse.Create(ServiceErrorCodes.Validation, "请求参数缺失或格式不正确。", first.Key);
                };
            })
            .AddNewtonsoftJson(options =>
            {
                // 枚举序列化为字符串名称（与前端 Refit 默认行为对齐）
                options.SerializerSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
                // null 值字段仍然输出，保持 API 响应字段完整（前端不需要做额外的 null 判断）
                options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Include;
            });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            // 详情新旧链允许保留同名 DTO；Swagger 使用完整类型名隔离组件定义，避免短名称 SchemaId 冲突。
            options.CustomSchemaIds(type => type.FullName?.Replace('+', '.') ?? type.Name);
        });
        builder.Services.AddRouting(options => options.LowercaseUrls = true);
    }

    /// <summary>挂载追踪、异常、空错误响应补全、认证授权和 Controller 端点。</summary>
    public static void UseWebApiPipeline(this WebApplication app)
    {
        // ==========================================
        // 4. 配置 HTTP 请求管道 (Middlewares)
        // ==========================================

        // 追踪覆盖异常记录及响应生成，确保同一次请求使用相同标识。
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<GlobalExceptionMiddleware>();
        app.UseStatusCodePages(context =>
        {
            var code = context.HttpContext.Response.StatusCode switch
            {
                401 => ServiceErrorCodes.Unauthenticated,
                403 => ServiceErrorCodes.Forbidden,
                404 => ServiceErrorCodes.NotFound,
                _ => null
            };
            return code is null ? Task.CompletedTask
                : ServiceErrorResponse.WriteAsync(context.HttpContext, code, "请求的资源不存在或当前身份无法访问。");
        });

        // 图片只读入口不解析业务身份或访问数据库，仍服从宿主 HTTPS 跳转。
        app.UseHttpsRedirection();
        app.UseProductImages();

        // DevUser 免登录注入（仅 Development 且 UseDevUser=true 时激活）
        // 注意：必须在 UseAuthentication 之前，这样才能覆盖 HttpContext.User
        app.UseMiddleware<DevUserMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();   // ← 先认证（解析 JWT）
        app.UseAuthorization();    // ← 再授权
        app.MapControllers();
    }
}
