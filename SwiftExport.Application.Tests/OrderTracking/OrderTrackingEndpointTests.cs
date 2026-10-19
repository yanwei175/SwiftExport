using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.OrderTracking.Overview;
using SwiftExport.Application.Interfaces.OrderTracking.DemandDetail;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.Apis.OrderTracking;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.OrderTracking.DemandDetail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.Controllers.OrderTracking;
using SwiftExport.WebApi.ErrorHandling;

namespace SwiftExport.Application.Tests.OrderTracking;

/// <summary>真实 Refit 和 MVC 验证三个跟踪入口，独立权限拒绝路径不进入查询服务。</summary>
[TestClass]
public sealed class OrderTrackingEndpointTests
{
    /// <summary>仅跟踪权限可读总览、明细和客户候选；销售权限和匿名不能替代。</summary>
    [TestMethod]
    public async Task RefitRoutes_RequireTrackingPermissionAndPreserveDemandIdentity()
    {
        var user = new User(); var permission = new Permission(); var service = new Query();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IOrderTrackingOverviewService>(service);
        builder.Services.AddSingleton<IDemandTrackingHeaderService>(service);
        builder.Services.AddControllers(options =>
        {
            options.Filters.Add(new EndpointPermissionFilter(user, permission));
            options.Filters.Add<ServiceResultHttpFilter>();
        }).AddApplicationPart(typeof(OrderTrackingOverviewController).Assembly);
        await using var app = builder.Build(); app.MapControllers(); await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address + "/api/order-tracking") };
        var api = RestService.For<IOrderTrackingApi>(client);
        var id = Guid.NewGuid();
        var page = await api.SearchAsync(new() { CustomerCodes = ["A", "B"], SourceTypes = ["CR"] });
        Assert.IsTrue(page.Success); Assert.AreEqual("CR", service.Request!.SourceTypes.Single());
        CollectionAssert.AreEqual(new[] { "A", "B" }, service.Request.CustomerCodes);
        Assert.AreEqual(id, (await api.GetDemandHeaderAsync(id)).Data.GrossDemandId);
        CollectionAssert.AreEqual(new[] { "A", "B" }, (await api.GetCustomerCodesAsync()).Data);
        Assert.AreEqual(3, service.Calls);
        Assert.IsTrue(permission.Keys.All(key => key == PermissionKeys.OrderTrackingView));
        permission.Allowed = PermissionKeys.SalesOrderView;
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => api.SearchAsync(new()))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => api.GetDemandHeaderAsync(id))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<ApiException>(() => api.GetCustomerCodesAsync())).StatusCode);
        user.Authenticated = false; permission.Allowed = PermissionKeys.OrderTrackingView;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Assert.ThrowsAsync<ApiException>(() => api.SearchAsync(new()))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Assert.ThrowsAsync<ApiException>(() => api.GetDemandHeaderAsync(id))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Assert.ThrowsAsync<ApiException>(() => api.GetCustomerCodesAsync())).StatusCode);
        Assert.AreEqual(3, service.Calls);
    }

    /// <summary>允许同一 HTTP 宿主切换认证状态，不访问账号数据库。</summary>
    private sealed class User : ICurrentUser
    {
        internal bool Authenticated = true;
        public string UserName => "tracking-http";
        public Guid? UserId { get; } = Guid.NewGuid();
        public bool IsAuthenticated => Authenticated;
    }

    /// <summary>记录端点实际请求的键，独立模拟实时撤权。</summary>
    private sealed class Permission : IPermissionEvaluationService
    {
        internal string Allowed = PermissionKeys.OrderTrackingView;
        internal readonly List<string> Keys = [];
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <summary>只放行当前显式权限，销售权限不会隐式包含跟踪权限。</summary>
        public Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default)
        { Keys.Add(permissionKey); return Task.FromResult(permissionKey == Allowed); }
    }

    /// <summary>只记录真实路由是否进入业务端，不接触数据库。</summary>
    private sealed class Query : IOrderTrackingOverviewService, IDemandTrackingHeaderService
    {
        internal int Calls;
        internal OrderTrackingSearchRequest? Request;
        /// <summary>记录总览路由进入服务的次数及反序列化筛选。</summary>
        public Task<ServiceResult<PagedResponseBase<OrderTrackingListItemDto>>> SearchAsync(OrderTrackingSearchRequest request, CancellationToken cancellationToken = default)
        { Calls++; Request = request; return Task.FromResult(ServiceResult<PagedResponseBase<OrderTrackingListItemDto>>.Ok(new())); }
        /// <summary>返回固定客户代码，验证只持有跟踪权限也能完成候选读取。</summary>
        public Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(ServiceResult<List<string>>.Ok(["A", "B"])); }
        /// <summary>回显路径身份，验证 Refit 与 MVC 的稳定需求标识一致。</summary>
        public Task<ServiceResult<DemandTrackingHeaderDto>> GetDemandHeaderAsync(Guid grossDemandId, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(ServiceResult<DemandTrackingHeaderDto>.Ok(new() { GrossDemandId = grossDemandId })); }
    }
}
