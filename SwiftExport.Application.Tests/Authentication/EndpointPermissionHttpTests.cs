using System.Reflection;
using Refit;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Application.Interfaces.Customers.Management;
using SwiftExport.Application.Tests.Customers;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.SalesOrders.Query;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.Controllers.SalesOrders.Query;
using SwiftExport.WebApi.ErrorHandling;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>通过真实 MVC 路由和 HTTP 管道验证 Async 方法名的权限匹配。</summary>
[TestClass]
public sealed class EndpointPermissionHttpTests
{
    /// <summary>销售客户代码查询仅持有 View 键时可达业务服务，撤权后同路由返回 403。</summary>
    [TestMethod]
    public async Task AsyncNamedAction_UsesRealMvcDescriptorAndChecksExactPermission()
    {
        var permission = new PermissionStub();
        var service = new SalesQueryStub();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<ISalesOrderQueryService>(service);
        builder.Services.AddControllers(options =>
        {
            options.Filters.Add(new EndpointPermissionFilter(new UserStub(), permission));
            options.Filters.Add<ServiceResultHttpFilter>();
        }).AddApplicationPart(typeof(SalesOrderQueryController).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        permission.Allowed = true;
        using var allowed = await client.GetAsync("api/sales-orders/manager/customer-codes");
        Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode);
        StringAssert.Contains(await allowed.Content.ReadAsStringAsync(), "HTTP_PERMISSION_PROBE");
        Assert.AreEqual(1, service.Calls);

        permission.Allowed = false;
        using var denied = await client.GetAsync("api/sales-orders/manager/customer-codes");
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.AreEqual(1, service.Calls);
        CollectionAssert.AreEqual(new[] { "Sales.Order.View", "Sales.Order.View" }, permission.Keys);
    }

    /// <summary>通过真实 Refit、HTTP 路由和权限过滤器读取完整客户快照，撤权后不进入服务。</summary>
    [TestMethod]
    public async Task CustomerGetAll_RefitAndHttpRoutePreserveSnapshotAndPermission()
    {
        var permission = new PermissionStub("Customers.Customer.View") { Allowed = true };
        var calls = 0;
        var rows = Enumerable.Range(0, 251).Select(i => new CustomerDto { Uid = Guid.NewGuid(), Version = 9,
            CustomerCode = $"C{i}", CustomerName = $"客户{i}" }).ToArray();
        var service = DispatchProxy.Create<ICustomerService, CustomerUpdateConcurrencyTests.RepositoryProbe>();
        ((CustomerUpdateConcurrencyTests.RepositoryProbe)(object)service).Handler = (method, args) =>
        {
            Assert.AreEqual("GetAllAsync", method.Name);
            Assert.IsTrue(((CancellationToken)args![0]!).CanBeCanceled);
            calls++;
            return Task.FromResult(ServiceResult<IReadOnlyList<CustomerDto>>.Ok(rows));
        };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(service);
        builder.Services.AddControllers(options =>
        {
            options.Filters.Add(new EndpointPermissionFilter(new UserStub(), permission));
            options.Filters.Add<ServiceResultHttpFilter>();
        }).AddApplicationPart(typeof(SalesOrderQueryController).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address + "/api/Customer") };
        var api = RestService.For<ICustomerApi>(client);
        var result = await api.GetAllAsync();
        Assert.IsTrue(result.Success);
        Assert.HasCount(251, result.Data);
        Assert.AreEqual(rows[250].Uid, result.Data[250].Uid);
        Assert.AreEqual(9, result.Data[250].Version);
        permission.Allowed = false;
        var denied = await Assert.ThrowsAsync<ApiException>(() => api.GetAllAsync());
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.AreEqual(1, calls);
    }

    /// <summary>提供有效身份以精确隔离业务权限，不访问数据库。</summary>
    private sealed class UserStub : ICurrentUser
    {
        public string UserName => "http-permission-test";
        public Guid? UserId { get; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
    }

    /// <summary>记录 HTTP 过滤器实际检查的键并模拟实时撤权。</summary>
    private sealed class PermissionStub(string requiredPermission = "Sales.Order.View") : IPermissionEvaluationService
    {
        public bool Allowed { get; set; }
        public List<string> Keys { get; } = [];
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default)
        {
            Keys.Add(permissionKey);
            return Task.FromResult(Allowed && permissionKey == requiredPermission);
        }
    }

    /// <summary>仅标记是否进入真实 Controller 的 Async 查询动作。</summary>
    private sealed class SalesQueryStub : ISalesOrderQueryService
    {
        public int Calls { get; private set; }
        public Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(ServiceResult<List<string>>.Ok(["HTTP_PERMISSION_PROBE"]));
        }
        public Task<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>> SearchAsync(SalesOrderManagerSearchRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<ServiceResult<SalesOrderDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync(SalesOrderProductSearchRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync(SalesOrderCustomerSearchRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
