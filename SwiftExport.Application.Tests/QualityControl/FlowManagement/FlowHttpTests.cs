using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.QualityControl.FlowManagement;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.Controllers.QualityControl.FlowManagement;
using SwiftExport.WebApi.ErrorHandling;

namespace SwiftExport.Application.Tests.QualityControl.FlowManagement;

/// <summary>以真实 MVC、认证和权限过滤器验证五个 HTTP 入口，所有持久化均由无数据库探针代替。</summary>
[TestClass]
public sealed class FlowHttpTests
{
    /// <summary>五种请求分别验证未登录、错误权限和正确权限，授权失败不能进入服务。</summary>
    [TestMethod]
    public async Task AllRoutesRequireAuthenticationAndExactOperationPermission()
    {
        var permissions = new PermissionProbe();
        var service = new ServiceProbe();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ICurrentUser, HttpUser>();
        builder.Services.AddSingleton<IPermissionEvaluationService>(permissions);
        builder.Services.AddSingleton<IFlowService>(service);
        builder.Services.AddAuthentication("FlowTest").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("FlowTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers(options =>
        {
            options.Filters.Add<EndpointPermissionFilter>();
            options.Filters.Add<ServiceResultHttpFilter>();
        }).AddApplicationPart(typeof(FlowController).Assembly);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var id = Guid.NewGuid();
        var edit = new FlowEditDto { MaterialId = Guid.NewGuid(), TestDate = new(2026, 10, 8),
            Lines = [new() { FunctionId = Guid.NewGuid(), LimiterSpecificationId = Guid.NewGuid(), Flow1BarLMin = 1 }] };
        var routes = new (HttpMethod Method, string Path, object? Body, string Key)[]
        {
            (HttpMethod.Post, "api/quality-control/flows/search", new FlowSearchRequest(), PermissionKeys.QualityControlFlowView),
            (HttpMethod.Get, $"api/quality-control/flows/{id}", null, PermissionKeys.QualityControlFlowView),
            (HttpMethod.Post, "api/quality-control/flows", new CreateItemRequest<FlowEditDto> { Data = edit }, PermissionKeys.QualityControlFlowCreate),
            (HttpMethod.Put, $"api/quality-control/flows/{id}", new UpdateItemRequest<FlowEditDto> { Uid = id, Version = 1, Data = edit }, PermissionKeys.QualityControlFlowEdit),
            (HttpMethod.Delete, $"api/quality-control/flows/{id}", new DeleteItemRequest { Uid = id, Version = 1 }, PermissionKeys.QualityControlFlowDelete)
        };
        foreach (var route in routes)
        {
            var before = service.Calls;
            using var anonymous = await Send(client, route.Method, route.Path, route.Body, false);
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode, route.Path);
            Assert.AreEqual(before, service.Calls);
            permissions.Key = "System.Users.Manage";
            using var denied = await Send(client, route.Method, route.Path, route.Body, true);
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode, route.Path);
            Assert.AreEqual(before, service.Calls);
            permissions.Key = route.Key;
            using var allowed = await Send(client, route.Method, route.Path, route.Body, true);
            Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode, await allowed.Content.ReadAsStringAsync());
            Assert.AreEqual(before + 1, service.Calls);
            CollectionAssert.AreEqual(new[] { route.Key, route.Key }, permissions.Requested.TakeLast(2).ToArray());
        }
        // 路径身份错配必须在服务转发前被拒绝，版本不会被错误头使用。
        permissions.Key = PermissionKeys.QualityControlFlowEdit;
        var calls = service.Calls;
        using var mismatch = await Send(client, HttpMethod.Put, $"api/quality-control/flows/{id}",
            new UpdateItemRequest<FlowEditDto> { Uid = Guid.NewGuid(), Version = 1, Data = edit }, true);
        Assert.AreEqual(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.AreEqual(calls, service.Calls);
        permissions.Key = PermissionKeys.QualityControlFlowDelete;
        using var deleteMismatch = await Send(client, HttpMethod.Delete, $"api/quality-control/flows/{id}",
            new DeleteItemRequest { Uid = Guid.NewGuid(), Version = 1 }, true);
        Assert.AreEqual(HttpStatusCode.BadRequest, deleteMismatch.StatusCode);
        Assert.AreEqual(calls, service.Calls);
    }

    /// <summary>生成真实 HTTP 请求，JSON 使用客户端常见的驼峰字段名。</summary>
    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? body, bool authenticated)
    {
        using var request = new HttpRequestMessage(method, path);
        if (authenticated) request.Headers.Add("X-Flow-Test-Identity", "authenticated");
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    /// <summary>测试身份只在内存宿主启用，通过请求头决定是否建立认证票据。</summary>
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        /// <summary>测试专用认证，不接触生产 JWT、账号或数据库。</summary>
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Flow-Test-Identity")) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,
                "00000000-0000-0000-0000-000000000001")], "FlowTest"));
            return Task.FromResult(AuthenticateResult.Success(new(principal, "FlowTest")));
        }
    }

    /// <summary>权限过滤器从真实请求认证状态读取身份。</summary>
    private sealed class HttpUser(IHttpContextAccessor accessor) : ICurrentUser
    {
        public string UserName => "flow-http-test";
        public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;
        public Guid? UserId => IsAuthenticated ? Guid.Parse("00000000-0000-0000-0000-000000000001") : null;
    }

    /// <summary>精确比较当前操作键，并记录过滤器实际请求的权限。</summary>
    private sealed class PermissionProbe : IPermissionEvaluationService
    {
        public string Key { get; set; } = "";
        public List<string> Requested { get; } = [];
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
        /// <summary>只允许本场景明确授予的操作键。</summary>
        public Task<bool> HasPermissionAsync(Guid id, string key, CancellationToken token = default)
        { Requested.Add(key); return Task.FromResult(key == Key); }
    }

    /// <summary>只观察请求是否进入服务，不模拟持久化或业务规则。</summary>
    private sealed class ServiceProbe : IFlowService
    {
        public int Calls { get; private set; }
        private Task<ServiceResult<T>> Hit<T>(T value) { Calls++; return Task.FromResult(ServiceResult<T>.Ok(value)); }
        /// <summary>返回空分页以标记查询转发。</summary>
        public Task<ServiceResult<PagedResponseBase<FlowListItemDto>>> GetPagedAsync(FlowSearchRequest request, CancellationToken token = default) => Hit(new PagedResponseBase<FlowListItemDto> { PageSize = 20 });
        /// <summary>返回身份以标记详情转发。</summary>
        public Task<ServiceResult<FlowDetailDto>> GetByIdAsync(Guid id, CancellationToken token = default) => Hit(new FlowDetailDto { Uid = id });
        /// <summary>返回空保存回执以标记新增转发。</summary>
        public Task<ServiceResult<FlowSaveResponse>> CreateAsync(CreateItemRequest<FlowEditDto> request, CancellationToken token = default) => Hit(new FlowSaveResponse());
        /// <summary>返回空保存回执以标记更新转发。</summary>
        public Task<ServiceResult<FlowSaveResponse>> UpdateAsync(UpdateItemRequest<FlowEditDto> request, CancellationToken token = default) => Hit(new FlowSaveResponse());
        /// <summary>返回身份以标记删除转发。</summary>
        public Task<ServiceResult<SaveItemResult>> DeleteAsync(DeleteItemRequest request, CancellationToken token = default) => Hit(new SaveItemResult { Uid = request.Uid, Success = true });
    }
}
