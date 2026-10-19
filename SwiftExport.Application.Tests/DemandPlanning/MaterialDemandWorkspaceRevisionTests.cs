using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Sources;
using SwiftExport.Application.Services.DemandPlanning.Materials.Query;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Confirmation;
using SwiftExport.Application.Tests.OrderTracking;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.Controllers.DemandPlanning.Materials.Query;
using SwiftExport.WebApi.ErrorHandling;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证物料管理必要摘要与采购详情的权限隔离，及采购确认的实物依据边界。</summary>
[TestClass]
public sealed class MaterialDemandWorkspaceRevisionTests
{
    /// <summary>技术版本推进不否决相同指纹，空白、缺失或真实变化的指纹不能确认采购。</summary>
    [TestMethod,DataRow(null),DataRow(""),DataRow(" "),DataRow("changed"),DataRow("BASIS")]
    public void Confirmation_RequiresExactNonemptyPhysicalBasis(string? invalid)
    {
        var head=new PurchaseOrderHead{Uid=Guid.NewGuid(),OriginKind=1};
        var line=new PurchaseOrderLine{Uid=Guid.NewGuid(),PurchaseOrderId=head.Uid,Quantity=6,UnlinkedQty=0};
        var source=new GrossDemand{Uid=Guid.NewGuid(),Version=9,DemandBasisFingerprint="basis"};
        var material=new NetDemand{Uid=Guid.NewGuid(),GrossDemandId=source.Uid,DecompositionRevision=1,SourceDemandVersionSnapshot=1,SourceDemandBasisFingerprintSnapshot="basis"};
        var facts=new ProcurementReversalFacts{Head=head,Lines=[line],Sources=[source],Demands=[material],Links=[new(){NetDemandId=material.Uid,TargetType="PO",TargetHeadId=head.Uid,TargetLineId=line.Uid,AllocatedQty=6}]};
        ProcurementConfirmationBuilder.RequireSourceReferences(facts);
        material.SourceDemandBasisFingerprintSnapshot=invalid;
        Assert.ThrowsExactly<InvalidOperationException>(()=>ProcurementConfirmationBuilder.RequireSourceReferences(facts));
    }

    /// <summary>仅物料权限保留数量/核实摘要，隐藏采购身份并在仓储前拒绝对应筛选。</summary>
    [TestMethod]
    public async Task MaterialSummary_DoesNotLeakPurchaseDetailsOrFilterOracle()
    {
        var repo=new Repository();var permissions=new OrderTrackingSummaryServiceTests.Permissions();var service=Service(repo,permissions);
        var result=await service.SearchManagerAsync(new());Assert.IsTrue(result.Success);Assert.AreEqual(6m,result.Data.Items.Single().AssociatedPurchaseQty);
        Assert.IsNull(result.Data.Items.Single().PurchaseOrderNos);Assert.IsNull(result.Data.Items.Single().ActualPurchaseSuppliers);Assert.IsFalse(repo.IncludePurchaseDetails);
        foreach(var request in new MaterialDemandManagerSearchRequest[]{new(){ActualPurchaseSupplierId=Guid.NewGuid()},new(){ActualPurchaseSupplierKeyword="secret"},new(){PurchaseOrderNo="PO-SECRET"}})
            Assert.AreEqual(ServiceErrorCodes.Forbidden,(await service.SearchManagerAsync(request)).Error.Code);
        Assert.AreEqual(1,repo.Calls);
        permissions.Keys.Add(PermissionKeys.ProcurementOrderRead);var allowed=await service.SearchManagerAsync(new(){PurchaseOrderNo="PO-SECRET"});
        Assert.IsTrue(allowed.Success);Assert.IsTrue(repo.IncludePurchaseDetails);Assert.AreEqual("PO-SECRET",allowed.Data.Items.Single().PurchaseOrderNos);
    }

    /// <summary>真实Refit/MVC验证权限缺失为403且不进入仓储，旧物料追踪路由已退出。</summary>
    [TestMethod]
    public async Task Http_EnforcesPurchaseDetailPermissionAndRetiresTrace()
    {
        var repo=new Repository();var permissions=new OrderTrackingSummaryServiceTests.Permissions{Keys=[PermissionKeys.DemandPlanningMaterialView]};
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IMaterialDemandQueryService>(Service(repo,permissions));
        var source=DispatchProxy.Create<IMaterialDemandSourceQueryService,OrderTrackingSliceEndpointTests.ServiceProbe>();
        ((OrderTrackingSliceEndpointTests.ServiceProbe)(object)source).Handler=method=>throw new AssertFailedException("意外来源查询："+method.Name);
        builder.Services.AddSingleton(source);
        builder.Services.AddControllers(options=>{options.Filters.Add(new EndpointPermissionFilter(new OrderTrackingSummaryServiceTests.User(),permissions));options.Filters.Add<ServiceResultHttpFilter>();})
            .AddApplicationPart(typeof(MaterialDemandQueryController).Assembly).AddNewtonsoftJson();
        await using var app=builder.Build();app.MapControllers();await app.StartAsync();
        var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client=new HttpClient{BaseAddress=new Uri(address+"/api/demand-planning/net-demands")};var api=RestService.For<INetDemandManagerApi>(client);
        var basic=await api.SearchAsync(new());Assert.IsTrue(basic.Success);Assert.IsNull(basic.Data.Items.Single().PurchaseOrderNos);
        Assert.AreEqual(HttpStatusCode.Forbidden,(await Assert.ThrowsAsync<ApiException>(()=>api.SearchAsync(new(){PurchaseOrderNo="PO-SECRET"}))).StatusCode);Assert.AreEqual(1,repo.Calls);
        permissions.Keys.Add(PermissionKeys.ProcurementOrderRead);Assert.AreEqual("PO-SECRET",(await api.SearchAsync(new(){PurchaseOrderNo="PO-SECRET"})).Data.Items.Single().PurchaseOrderNos);
        using var retired=await client.GetAsync($"/api/demand-planning/net-demands/{Guid.NewGuid()}/trace");Assert.AreEqual(HttpStatusCode.NotFound,retired.StatusCode);
        permissions.Keys.Clear();Assert.AreEqual(HttpStatusCode.Forbidden,(await Assert.ThrowsAsync<ApiException>(()=>api.SearchAsync(new()))).StatusCode);Assert.AreEqual(2,repo.Calls);
    }

    private static MaterialDemandQueryService Service(Repository repo,OrderTrackingSummaryServiceTests.Permissions permissions)=>new(new ServiceBaseContext(null!,NullLoggerFactory.Instance),repo,new OrderTrackingSummaryServiceTests.User(),permissions);
    /// <summary>仅记录查询边界，不复制真实SQL筛选及守恒算法。</summary>
    private sealed class Repository:IMaterialDemandQueryRepository
    {
        internal int Calls;internal bool IncludePurchaseDetails;
        /// <inheritdoc />
        public Task<PagedResponseBase<MaterialDemandManagerItemDto>> SearchManagerAsync(MaterialDemandManagerSearchRequest request,CancellationToken token=default,bool includePurchaseDetails=true)
        {Calls++;IncludePurchaseDetails=includePurchaseDetails;return Task.FromResult(new PagedResponseBase<MaterialDemandManagerItemDto>{Items=[new(){AssociatedPurchaseQty=6,PurchaseOrderNos="PO-SECRET",ActualPurchaseSuppliers="SUPPLIER-SECRET",ProcurementStatus="已承接"}]});}
        /// <inheritdoc />
        public Task<IReadOnlyList<NetDemandSourceGroupDto>> SearchMaterialSourceGroupsAsync(MaterialDemandSourceGroupSearchRequest request,CancellationToken token=default)=>throw new NotSupportedException();
    }
}
