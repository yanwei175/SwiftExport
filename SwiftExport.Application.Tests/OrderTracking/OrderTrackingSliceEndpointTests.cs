using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.OrderTracking.Sources;
using SwiftExport.Application.Interfaces.OrderTracking.Materials;
using SwiftExport.Application.Interfaces.OrderTracking.Purchases;
using SwiftExport.Application.Interfaces.OrderTracking.Receipts;
using SwiftExport.Application.Interfaces.OrderTracking.Deliveries;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.Apis.OrderTracking;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.Controllers.OrderTracking;
using SwiftExport.WebApi.ErrorHandling;

namespace SwiftExport.Application.Tests.OrderTracking;

/// <summary>通过真实 Refit/MVC 验证新来源接口与四区域的 AND 权限，不只检查映射字典。</summary>
[TestClass]
public sealed class OrderTrackingSliceEndpointTests
{
    /// <summary>每个所需键缺失或匿名时均不进入服务，全部满足时路由与序列化可达。</summary>
    [TestMethod]
    public async Task Routes_RequireEveryPermissionBeforeService()
    {
        var user=new User();var permissions=new Permissions();var calls=0;
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production"});
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        foreach(var type in new[]{typeof(IOrderTrackingSourceService),typeof(IDemandTrackingMaterialService),typeof(IDemandTrackingPurchaseService),
            typeof(IDemandTrackingReceiptService),typeof(IDemandTrackingDeliveryService)})
        {
            var proxy=DispatchProxy.Create(type,typeof(ServiceProbe));
            ((ServiceProbe)proxy).Handler=method=>
            {
                calls++;
                var resultType=method.ReturnType.GetGenericArguments().Single();
                var result=Activator.CreateInstance(resultType)!;
                resultType.GetProperty("Success")!.SetValue(result,true);
                var dataType=resultType.GetProperty("Data")!.PropertyType;
                resultType.GetProperty("Data")!.SetValue(result,Activator.CreateInstance(dataType));
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType).Invoke(null,[result])!;
            };
            builder.Services.AddSingleton(type,proxy);
        }
        builder.Services.AddControllers(options=>
        {
            options.Filters.Add(new EndpointPermissionFilter(user,permissions));
            options.Filters.Add<ServiceResultHttpFilter>();
        }).AddApplicationPart(typeof(OrderTrackingSourceController).Assembly);
        await using var app=builder.Build();app.MapControllers();await app.StartAsync();
        var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client=new HttpClient{BaseAddress=new Uri(address+"/api/order-tracking")};
        var api=RestService.For<IOrderTrackingApi>(client);var id=Guid.NewGuid();
        var cases=new (Func<Task> Read,string[] Keys)[]
        {
            (()=>api.SearchSourcesAsync(new(){Keyword="~%_?"}),[PermissionKeys.OrderTrackingView]),
            (()=>api.GetMaterialsAsync(id),[PermissionKeys.OrderTrackingView,PermissionKeys.DemandPlanningMaterialView]),
            (()=>api.GetPurchasesAsync(id),[PermissionKeys.OrderTrackingView,PermissionKeys.ProcurementOrderRead]),
            (()=>api.GetReceiptsAsync(id),[PermissionKeys.OrderTrackingView,PermissionKeys.InventoryInboundView,PermissionKeys.ProcurementOrderRead]),
            (()=>api.GetDeliveriesAsync(id),[PermissionKeys.OrderTrackingView,PermissionKeys.ShippingPackingListView])
        };
        foreach(var item in cases)
        {
            permissions.Allowed=item.Keys.ToHashSet(StringComparer.Ordinal);user.Authenticated=true;
            var before=calls;await item.Read();Assert.AreEqual(before+1,calls);
            foreach(var missing in item.Keys)
            {
                permissions.Allowed=item.Keys.Where(key=>key!=missing).ToHashSet(StringComparer.Ordinal);
                var denied=await Assert.ThrowsAsync<ApiException>(item.Read);
                Assert.AreEqual(HttpStatusCode.Forbidden,denied.StatusCode,missing);Assert.AreEqual(before+1,calls);
            }
            user.Authenticated=false;permissions.Allowed=item.Keys.ToHashSet(StringComparer.Ordinal);
            Assert.AreEqual(HttpStatusCode.Unauthorized,(await Assert.ThrowsAsync<ApiException>(item.Read)).StatusCode);
            Assert.AreEqual(before+1,calls);
        }
        Assert.AreEqual(5,calls);
    }

    /// <summary>由真实 Controller 调用，只有通过权限过滤后才返回窄服务结果。</summary>
    public class ServiceProbe : DispatchProxy
    {
        internal Func<MethodInfo,object>? Handler;
        /// <summary>记录是否进入服务，不执行数据库操作。</summary>
        protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>Handler!(targetMethod!);
    }
    /// <summary>同一宿主内切换登录状态，隔离权限测试。</summary>
    private sealed class User : ICurrentUser
    {
        internal bool Authenticated=true;
        public string UserName=>"tracking-region-http";
        public Guid? UserId {get;}=Guid.NewGuid();
        public bool IsAuthenticated=>Authenticated;
    }
    /// <summary>只放行当前显式集合，验证 AND 条件中每个键都不可缺少。</summary>
    private sealed class Permissions : IPermissionEvaluationService
    {
        internal HashSet<string> Allowed=[];
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        /// <summary>按精确键答复，不将模块读权限扩为跟踪权限。</summary>
        public Task<bool> HasPermissionAsync(Guid userId,string permissionKey,CancellationToken cancellationToken=default)=>Task.FromResult(Allowed.Contains(permissionKey));
    }
}
