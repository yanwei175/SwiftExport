using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
using SwiftExport.WebApi.OpenApi;
using Microsoft.OpenApi;
using System.Text;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace SwiftExport.WebApi.Configuration;

/// <summary>集中组合 WebApi 的依赖、认证、序列化和请求管道，统一注册公共 HTTP 边界。</summary>
public static class WebApiConfiguration
{
    /// <summary>注册后端依赖、JWT 身份验证和 HTTP 框架选项；沿用宿主配置。</summary>
    public static void AddWebApiServices(this WebApplicationBuilder builder)
    {
        // Caddy 与后端在同一服务器；只接受回环代理传入的协议和客户端地址，不信任局域网直连伪造头。
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.KnownProxies.Add(IPAddress.Loopback);
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
        });

        // 1. 注册基础设施
        builder.Services.AddInfrastructureServices();

        // 2. 注册应用逻辑
        builder.Services.AddApplicationServices();

        // RootPath 非空时注册通用本地文件存储；Enabled 仍只控制产品图片 HTTP 入口。
        builder.Services.AddConfiguredFileStorage(builder.Configuration);

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

        // 未显式标记匿名的 Controller 端点默认要求有效 JWT；业务权限仍逐端点核验。
        builder.Services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        // HttpContextAccessor 是 ICurrentUser 的依赖，必须注册
        builder.Services.AddHttpContextAccessor();
        // 注册 ICurrentUser（Scoped，每次请求从 HttpContext 读取）
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
        builder.Services.AddSingleton<IAuthTokenFactory, AuthTokenFactory>();
        builder.Services.AddScoped<CredentialVersionTokenValidator>();
        builder.Services.AddScoped<EndpointPermissionFilter>();

        // ══════════════════════════════════════════════════════
        // 4. 注册 Web API 核心服务
        // ══════════════════════════════════════════════════════

        // 中文注释：注册内存缓存，供 PreventDuplicateRequestAttribute 防抖 Filter 使用。
        builder.Services.AddMemoryCache();
        // 中文注释：统一使用 Newtonsoft.Json，与前端 Refit（.NET Framework 4.8）行为完全一致。
        // 枚举由显式 Converter 输出字符串，字段使用 MVC 默认驼峰命名，并保留 null。
        builder.Services.AddControllers(options =>
            {
                options.Filters.Add<ServiceResultHttpFilter>();
                options.Filters.AddService<EndpointPermissionFilter>();
            })
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
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "SwiftExport ERP API", Version = "v1" });
            // 保留同名 DTO 的命名空间，封闭泛型不输出程序集限定名。
            options.CustomSchemaIds(WebContractSchemaIds.Create);
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
                Description = "使用登录返回的 access_token；服务器实时核验凭据版本和操作权限。"
            });
            options.OperationFilter<FirstWebContractOperationFilter>();
            options.DocumentFilter<FirstWebContractOperationFilter>();
            options.UseAllOfToExtendReferenceSchemas();
        });
        // schema 使用与实际 MVC 相同的 Newtonsoft 契约和枚举转换器。
        builder.Services.AddSwaggerGenNewtonsoftSupport();
        builder.Services.AddRouting(options => options.LowercaseUrls = true);
    }

    /// <summary>挂载追踪、异常、空错误响应补全、认证授权和 Controller 端点。</summary>
    public static void UseWebApiPipeline(this WebApplication app)
    {
        // 必须早于 HTTPS 跳转；否则 Caddy 的 HTTPS 请求到达 HTTP 后端后会反复重定向。
        app.UseForwardedHeaders();

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
