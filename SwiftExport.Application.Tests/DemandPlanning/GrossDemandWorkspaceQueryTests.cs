using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using Refit;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Application.Services.DemandPlanning.Sources.Query;
using SwiftExport.Application.Tests.OrderTracking;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.Controllers.DemandPlanning.Sources.Query;
using SwiftExport.WebApi.ErrorHandling;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证成品需求本体两种查询模式、客户候选和旧追踪退出，不执行命令或写库。</summary>
[TestClass]
public sealed class GrossDemandWorkspaceQueryTests
{
    /// <summary>无效身份、来源、状态及倒置日期在读取前拒绝；合法请求不被回写。</summary>
    [TestMethod]
    public async Task Validation_RejectsInvalidCriteriaAndPreservesAppliedRequest()
    {
        var repo=new Repository();var service=Service(repo);
        foreach(var request in new GrossDemandSearchRequest[]{new(){SourceUid=Guid.Empty},new(){SourceTypes=["so"]},new(){SourceTypes=[null!]},
            new(){CustomerIds=[Guid.Empty]},new(){LifecycleStatuses=[(GrossDemandLifecycleStatusEnum)1]},new(){RequiredDateFrom=new(2026,10,20),RequiredDateTo=new(2026,10,19)}})
            Assert.AreEqual(ServiceErrorCodes.Validation,(await service.SearchAsync(request)).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Validation,(await service.SearchSourceGroupsAsync(new(){LifecycleStatuses=[(GrossDemandLifecycleStatusEnum)999]})).Error.Code);
        Assert.AreEqual(0,repo.Calls);
        var valid=new GrossDemandSearchRequest{SourceUid=Guid.NewGuid(),SourceTypes=["SO","CR","MD"],PageIndex=8,PageSize=1,CustomerIds=[Guid.NewGuid()]};
        var before=JsonSerializer.Serialize(valid);Assert.IsTrue((await service.SearchAsync(valid)).Success);
        Assert.AreSame(valid,repo.Search);Assert.AreEqual(before,JsonSerializer.Serialize(valid));
    }

    /// <summary>指定来源返回302行；所有本体筛选仍生效，参数显式绑定且没有下游或事务副作用。</summary>
    [TestMethod]
    public async Task SelectedSource_ReadsAllRowsWithExactCustomerAndLiteralKeyword()
    {
        using var f=new OrderTrackingReadFixture(true);var repo=new PostgreSqlGrossDemandQueryRepository(f.Context);var source=Guid.NewGuid();var customer=Guid.NewGuid();
        var rows=Enumerable.Range(0,302).Select(_=>new GrossDemandListItemDto{Uid=Guid.NewGuid(),SourceUid=source,CustomerId=customer,LinkSku="中性%_?"}).ToArray();
        f.Connection.Results.Enqueue(OrderTrackingReadFixture.Table(rows.Cast<object>().ToArray()));
        f.Connection.Results.Enqueue(OrderTrackingReadFixture.Table(new{Count=302}));
        using var cancellation=new CancellationTokenSource();
        var result=await repo.SearchWorkspaceAsync(new(){SourceUid=source,PageIndex=9,PageSize=1,CustomerIds=[customer,customer],Keyword=" ~%_? A ",
            SourceTypes=["SO"],SourceNos=["SO-1"],Skus=["SKU"],CustomerSkus=["C-SKU"],LifecycleStatuses=[GrossDemandLifecycleStatusEnum.Active],
            RequiredDateFrom=new(2026,10,19,15,30,0),RequiredDateTo=new(2026,10,20)},cancellation.Token);
        Assert.HasCount(302,result.Items);Assert.AreEqual(302,result.TotalCount);Assert.AreEqual(1,result.PageIndex);Assert.AreEqual(302,result.PageSize);
        foreach(var command in f.Connection.Commands)
        {
            Assert.AreSame(f.Session.Transaction,command.Transaction);Assert.AreEqual(source,command.Value("SourceUid"));
            CollectionAssert.AreEqual(new[]{customer},(Guid[])command.Value("CustomerIds")!);
            Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Uuid,command.Type("CustomerIds"));
            Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Integer,command.Type("LifecycleStatuses"));
            Assert.AreEqual("%~~~%~_? A%",command.Value("Keyword"));Assert.AreEqual(new DateTime(2026,10,19),command.Value("RequiredDateFrom"));
            StringAssert.Contains(command.Sql,"d.link_sku_snapshot");StringAssert.Contains(command.Sql,"d.customer_id=ANY(@CustomerIds)");
            foreach(var forbidden in new[]{"LIMIT","OFFSET","net_demand","purchase_order","packing_list","public.customer","HasActiveMaterial","HasValidPurchase","HasPostedDelivery"})
                Assert.IsFalse(command.Sql.Contains(forbidden,StringComparison.OrdinalIgnoreCase),forbidden);
        }
        Assert.HasCount(2,f.Connection.Commands);Assert.AreEqual(cancellation.Token,f.Connection.Token);
        Assert.AreEqual(0,f.Connection.Commits+f.Connection.Rollbacks);
    }

    /// <summary>普通模式分页稳定且大页码偏移不溢出；候选单次读取身份和显示字段。</summary>
    [TestMethod]
    public async Task OrdinaryPagingAndCustomerOptions_KeepTheirScopes()
    {
        using var f=new OrderTrackingReadFixture();var repo=new PostgreSqlGrossDemandQueryRepository(f.Context);
        f.Connection.Results.Enqueue(new System.Data.DataTable());f.Connection.Results.Enqueue(OrderTrackingReadFixture.Table(new{Count=0}));
        var page=await repo.SearchWorkspaceAsync(new(){PageIndex=int.MaxValue,PageSize=999});Assert.AreEqual(200,page.PageSize);
        var query=f.Connection.Commands[0];Assert.AreEqual((long)(int.MaxValue-1)*200,query.Value("Offset"));StringAssert.Contains(query.Sql,"LIMIT @PageSize OFFSET @Offset");
        var option=new GrossDemandCustomerOptionDto{Id=Guid.NewGuid(),Code="客户代码",Name="客户名称"};
        f.Connection.Results.Enqueue(OrderTrackingReadFixture.Table(option));var options=await repo.GetCustomerOptionsAsync();
        Assert.AreEqual(option.Id,options.Single().Id);Assert.AreEqual(option.Code,options.Single().Code);Assert.AreEqual(option.Name,options.Single().Name);
        var command=f.Connection.Commands.Last();StringAssert.Contains(command.Sql,"c.is_deleted=FALSE");StringAssert.Contains(command.Sql,"d.is_deleted=FALSE");
        Assert.IsFalse(command.Sql.Contains("lifecycle_status",StringComparison.Ordinal));Assert.IsFalse(command.Sql.Contains("LIMIT",StringComparison.Ordinal));
        Assert.AreEqual(3,f.Connection.Commands.Count);
    }

    /// <summary>真实Refit/MVC验证客户候选只要求成品需求查看权限，旧追踪404，筛选原样到服务。</summary>
    [TestMethod]
    public async Task Http_UsesSourcePermissionAndRetiresOldTrace()
    {
        var repo=new Repository();var permissions=new OrderTrackingSummaryServiceTests.Permissions{Keys=[PermissionKeys.DemandPlanningSourceView]};
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IGrossDemandQueryService>(Service(repo));
        builder.Services.AddControllers(options=>{options.Filters.Add(new EndpointPermissionFilter(new OrderTrackingSummaryServiceTests.User(),permissions));options.Filters.Add<ServiceResultHttpFilter>();})
            .AddApplicationPart(typeof(GrossDemandQueryController).Assembly).AddNewtonsoftJson(options=>options.SerializerSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter()));
        await using var app=builder.Build();app.MapControllers();await app.StartAsync();
        var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client=new HttpClient{BaseAddress=new Uri(address+"/api/demand-planning/gross-demands")};var api=RestService.For<IGrossDemandWorkspaceApi>(client);
        Assert.IsTrue((await api.GetCustomerOptionsAsync()).Success);Assert.AreEqual(1,repo.Calls);
        var request=new GrossDemandSearchRequest{SourceTypes=["CR"],CustomerIds=[Guid.NewGuid(),Guid.NewGuid()],Keyword="普通%_? 文本",SourceUid=Guid.NewGuid(),LifecycleStatuses=[GrossDemandLifecycleStatusEnum.Closed]};
        Assert.IsTrue((await api.SearchAsync(request)).Success);Assert.AreEqual(JsonSerializer.Serialize(request),JsonSerializer.Serialize(repo.Search));
        permissions.Keys.Clear();Assert.AreEqual(HttpStatusCode.Forbidden,(await Assert.ThrowsAsync<ApiException>(()=>api.GetCustomerOptionsAsync())).StatusCode);Assert.AreEqual(2,repo.Calls);
        Assert.AreEqual(HttpStatusCode.Forbidden,(await Assert.ThrowsAsync<ApiException>(()=>api.SearchAsync(new()))).StatusCode);Assert.AreEqual(2,repo.Calls);
        using var retired=await client.GetAsync($"/api/demand-planning/gross-demands/{Guid.NewGuid()}/trace");Assert.AreEqual(HttpStatusCode.NotFound,retired.StatusCode);
    }

    /// <summary>预取消零读取，客户候选技术失败不变成成功空集合。</summary>
    [TestMethod]
    public async Task CancellationAndFailure_PreserveBoundary()
    {
        var repo=new Repository();var service=Service(repo);var token=new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(()=>service.SearchAsync(new(),token));
        await Assert.ThrowsAsync<OperationCanceledException>(()=>service.SearchSourceGroupsAsync(new(),token));
        await Assert.ThrowsAsync<OperationCanceledException>(()=>service.GetCustomerOptionsAsync(token));Assert.AreEqual(0,repo.Calls);
        repo.Failure=new InvalidOperationException("内部连接信息");var failed=await service.GetCustomerOptionsAsync();
        Assert.IsFalse(failed.Success);Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable,failed.Error.Code);Assert.IsFalse(failed.Message.Contains("内部连接信息",StringComparison.Ordinal));
    }

    private static GrossDemandQueryService Service(Repository repo)=>new(new ServiceBaseContext(null!,NullLoggerFactory.Instance),repo);
    /// <summary>只记录服务和HTTP入参，不在替身内实现任何筛选算法。</summary>
    private sealed class Repository:IGrossDemandQueryRepository
    {
        internal int Calls;internal GrossDemandSearchRequest? Search;internal Exception? Failure;
        /// <inheritdoc />
        public Task<List<GrossDemandCustomerOptionDto>> GetCustomerOptionsAsync(CancellationToken token=default){Calls++;if(Failure is not null)throw Failure;return Task.FromResult(new List<GrossDemandCustomerOptionDto>());}
        /// <inheritdoc />
        public Task<PagedResponseBase<GrossDemandListItemDto>> SearchWorkspaceAsync(GrossDemandSearchRequest request,CancellationToken token=default){Calls++;Search=request;return Task.FromResult(new PagedResponseBase<GrossDemandListItemDto>());}
        /// <inheritdoc />
        public Task<IReadOnlyList<GrossDemandSourceGroupDto>> SearchSourceGroupsAsync(GrossDemandSourceGroupSearchRequest request,CancellationToken token=default){Calls++;return Task.FromResult<IReadOnlyList<GrossDemandSourceGroupDto>>([]);}
        /// <inheritdoc />
        public Task<GrossDemandDetailDto?> GetWorkspaceDetailAsync(Guid id,CancellationToken token=default)=>throw new NotSupportedException();
    }
}
