using System.Text.Json;
using System.Text;
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
using SwiftExport.Application.Interfaces.OrderTracking.Sources;
using SwiftExport.Application.Services.OrderTracking.Sources;
using SwiftExport.Contracts.Apis.OrderTracking;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.OrderTracking.Sources;
using SwiftExport.WebApi.Controllers.OrderTracking;
using SwiftExport.WebApi.ErrorHandling;

namespace SwiftExport.Application.Tests.OrderTracking;

/// <summary>验证组内成品需求匹配的输入、完整集合、错误语义及真实HTTP契约；SQL语义另由只读PG矩阵证明。</summary>
[TestClass]
public sealed class OrderTrackingSourceMatchesTests
{
    /// <summary>非法来源身份、筛选或关注码都在仓储前拒绝，不扩大为全部来源。</summary>
    [TestMethod]
    public async Task InvalidCriteria_DoNotEnterRepository()
    {
        var repo=new Repository();var service=Service(repo);var id=Guid.NewGuid();
        foreach(var source in new string?[]{null,"","so","ZZ"})
            Assert.AreEqual(ServiceErrorCodes.Validation,(await service.GetSourceMatchesAsync(source!,id,new())).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Validation,(await service.GetSourceMatchesAsync("SO",Guid.Empty,new())).Error.Code);
        foreach(var request in new OrderTrackingSourceMatchRequest?[]{null,new(){SourceTypes=["ZZ"]},
            new(){LifecycleStatuses=[(GrossDemandLifecycleStatusEnum)999]},new(){AttentionCodes=["Unknown"]},
            new(){RequiredDateFrom=new(2026,10,20),RequiredDateTo=new(2026,10,19)}})
            Assert.AreEqual(ServiceErrorCodes.Validation,(await service.GetSourceMatchesAsync("SO",id,request!)).Error.Code);
        Assert.AreEqual(0,repo.Calls);
    }

    /// <summary>保留完整身份、来源及本次业务日，不改写请求；空集合和技术失败保持不同语义。</summary>
    [TestMethod]
    public async Task CompleteMatches_KeepMetadataAndDistinguishFailure()
    {
        var repo=new Repository{Ids=Enumerable.Range(0,302).Select(_=>Guid.NewGuid()).ToList()};
        var service=Service(repo);var id=Guid.NewGuid();
        var request=new OrderTrackingSourceMatchRequest{SourceTypes=["SO","CR"],CustomerCodes=[" A "],Keyword="~%_?",AttentionCodes=["Overdue"]};
        var before=JsonSerializer.Serialize(request);using var cancellation=new CancellationTokenSource();
        var response=await service.GetSourceMatchesAsync("SO",id,request,cancellation.Token);
        Assert.IsTrue(response.Success);Assert.HasCount(302,response.Data.MatchedGrossDemandIds);
        Assert.AreEqual("SO",response.Data.SourceType);Assert.AreEqual(id,response.Data.SourceGroupId);
        Assert.AreEqual(repo.Date,response.Data.BusinessDate);Assert.AreEqual(repo.Date.Date,repo.Date);
        Assert.AreSame(request,repo.Request);Assert.AreEqual(before,JsonSerializer.Serialize(request));Assert.AreEqual(1,repo.Calls);
        Assert.AreEqual(cancellation.Token,repo.Token);
        repo.Ids=[];var empty=await service.GetSourceMatchesAsync("CR",id,new());
        Assert.IsTrue(empty.Success);Assert.HasCount(0,empty.Data.MatchedGrossDemandIds);
        repo.Failure=new InvalidOperationException("内部连接信息");
        var failed=await service.GetSourceMatchesAsync("MD",id,new());
        Assert.IsFalse(failed.Success);Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable,failed.Error.Code);
        Assert.IsFalse(failed.Message.Contains("内部连接信息",StringComparison.Ordinal));
    }

    /// <summary>仓储前与执行中的取消均向上传播，不转换成成功空集合或服务失败。</summary>
    [TestMethod]
    public async Task Cancellation_PropagatesBeforeAndDuringRead()
    {
        var repo=new Repository();var service=Service(repo);var id=Guid.NewGuid();
        await Assert.ThrowsAsync<OperationCanceledException>(()=>service.GetSourceMatchesAsync("SO",id,new(),new CancellationToken(true)));
        Assert.AreEqual(0,repo.Calls);
        using var cancellation=new CancellationTokenSource();repo.BeforeRead=()=>cancellation.Cancel();repo.Failure=new OperationCanceledException(cancellation.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(()=>service.GetSourceMatchesAsync("SO",id,new(),cancellation.Token));
        Assert.AreEqual(1,repo.Calls);
    }

    /// <summary>真实Dapper单次映射全量身份并透传外层事务、日期、数组及字面关键词参数。</summary>
    [TestMethod]
    public async Task Repository_UsesOneUnpagedParameterizedRead()
    {
        using var f=new OrderTrackingReadFixture(transaction:true);var id=Guid.NewGuid();
        var ids=Enumerable.Range(0,302).Select(_=>new{GrossDemandId=Guid.NewGuid()}).ToArray();
        f.Connection.Results.Enqueue(OrderTrackingReadFixture.Table(ids.Cast<object>().ToArray()));
        using var cancellation=new CancellationTokenSource();
        var response=await f.Sources.GetSourceMatchesAsync("SO",id,new(){Keyword=" ~%_? ",SourceTypes=["SO","CR"],
            CustomerCodes=[" A "],LifecycleStatuses=[GrossDemandLifecycleStatusEnum.Active],AttentionCodes=["Overdue"]},OrderTrackingReadFixture.BusinessDate,cancellation.Token);
        CollectionAssert.AreEqual(ids.Select(row=>row.GrossDemandId).ToArray(),response);
        var command=f.Connection.Commands.Single();Assert.AreSame(f.Session.Transaction,command.Transaction);
        Assert.AreEqual("SO",command.Value("SourceType"));Assert.AreEqual(id,command.Value("SourceGroupId"));
        Assert.AreEqual("%~~~%~_?%",command.Value("Keyword"));Assert.AreEqual(OrderTrackingReadFixture.BusinessDate,command.Value("BusinessDate"));
        Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Integer,command.Type("LifecycleStatuses"));
        Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Text,command.Type("SourceTypes"));
        StringAssert.Contains(command.Sql,"SELECT DISTINCT g.uid");StringAssert.Contains(command.Sql,"summary_attention");
        Assert.IsFalse(command.Sql.Contains("LIMIT",StringComparison.OrdinalIgnoreCase));Assert.IsFalse(command.Sql.Contains("OFFSET",StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(cancellation.Token,f.Connection.Token);Assert.AreEqual(0,f.Connection.Commits);Assert.AreEqual(0,f.Connection.Rollbacks);
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Sources.GetSourceMatchesAsync("SO",id,new(),OrderTrackingReadFixture.BusinessDate,new CancellationToken(true)));
        Assert.HasCount(1,f.Connection.Commands);
    }

    /// <summary>真实Refit与MVC绑定独立请求、稳定身份和完整响应；权限矩阵由真实过滤器专项覆盖。</summary>
    [TestMethod]
    public async Task RefitMvc_RoundTripsMatchesContract()
    {
        var repo=new Repository{Ids=Enumerable.Range(0,302).Select(_=>Guid.NewGuid()).ToList()};
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production"});
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IOrderTrackingSourceService>(Service(repo));
        builder.Services.AddControllers(options=>options.Filters.Add<ServiceResultHttpFilter>())
            .AddApplicationPart(typeof(OrderTrackingSourceController).Assembly)
            .AddNewtonsoftJson(options=>options.SerializerSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter()));
        await using var app=builder.Build();app.MapControllers();await app.StartAsync();
        var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client=new HttpClient{BaseAddress=new Uri(address+"/api/order-tracking")};var api=RestService.For<IOrderTrackingApi>(client);
        var id=Guid.NewGuid();var request=new OrderTrackingSourceMatchRequest{Keyword="字面%_?",SourceTypes=["CR"],CustomerCodes=["A"],
            RequiredDateFrom=new(2026,10,19),RequiredDateTo=new(2026,10,20),LifecycleStatuses=[GrossDemandLifecycleStatusEnum.Active],AttentionCodes=["MissingRequiredDate","SourceReview"]};
        var response=await api.GetSourceMatchesAsync("CR",id,request);
        Assert.IsTrue(response.Success);Assert.AreEqual("CR",response.Data.SourceType);Assert.AreEqual(id,response.Data.SourceGroupId);
        CollectionAssert.AreEqual(repo.Ids,response.Data.MatchedGrossDemandIds);Assert.AreEqual(repo.Date,response.Data.BusinessDate);
        Assert.AreEqual(JsonSerializer.Serialize(request),JsonSerializer.Serialize(repo.Request));Assert.AreEqual(1,repo.Calls);
        var search=new OrderTrackingSourceSearchRequest{SourceTypes=["CR"],Keyword="字面%_?"};
        Assert.IsTrue((await api.SearchSourcesAsync(search)).Success);
        Assert.AreEqual(JsonSerializer.Serialize(search),JsonSerializer.Serialize(repo.SearchRequest));Assert.AreEqual(2,repo.Calls);
        // 生产Newtonsoft默认会填充已有集合；省略、null、空集合及显式类型必须保持各自语义。
        var cases=new (string Body,string[]? Types)[]{("{}",null),("{\"sourceTypes\":null}",null),
            ("{\"sourceTypes\":[]}",[]),("{\"sourceTypes\":[\"CR\"]}",["CR"]),("{\"sourceTypes\":[\"SO\"]}",["SO"])};
        foreach(var item in cases)
        foreach(var matches in new[]{false,true})
        {
            using var content=new StringContent(item.Body,Encoding.UTF8,"application/json");
            using var result=await client.PostAsync(matches?$"/api/order-tracking/sources/CR/{id}/matches":"/api/order-tracking/sources/search",content);
            result.EnsureSuccessStatusCode();
            var actual=matches?repo.Request!.SourceTypes:repo.SearchRequest!.SourceTypes;
            if(item.Types is null)Assert.IsNull(actual,item.Body);
            else CollectionAssert.AreEqual(item.Types,actual.ToArray(),item.Body);
        }
        Assert.AreEqual(12,repo.Calls);
    }

    private static OrderTrackingSourceService Service(Repository repo)=>new(new ServiceBaseContext(null!,NullLoggerFactory.Instance),repo);

    /// <summary>只记录匹配用例入参、响应和失败，不在替身内复制SQL谓词。</summary>
    private sealed class Repository:IOrderTrackingSourceRepository
    {
        internal int Calls;internal List<Guid> Ids=[];internal DateTime Date;internal OrderTrackingSourceMatchRequest? Request;
        internal OrderTrackingSourceSearchRequest? SearchRequest;
        internal CancellationToken Token;internal Exception? Failure;internal Action? BeforeRead;
        /// <summary>记录单次匹配读取供服务及HTTP验证，不触碰数据库。</summary>
        public Task<List<Guid>> GetSourceMatchesAsync(string type,Guid id,OrderTrackingSourceMatchRequest request,DateTime date,CancellationToken token=default)
        {Calls++;Request=request;Date=date;Token=token;BeforeRead?.Invoke();if(Failure is not null)throw Failure;return Task.FromResult(Ids);}
        /// <summary>只记录HTTP传输的搜索条件，不在替身内实现筛选或分页。</summary>
        public Task<PagedResponseBase<OrderTrackingSourceGroupDto>> SearchSourcesAsync(OrderTrackingSourceSearchRequest request,DateTime date,CancellationToken token=default)
        {Calls++;SearchRequest=request;return Task.FromResult(new PagedResponseBase<OrderTrackingSourceGroupDto>());}
    }
}
