using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.OrderTracking.Overview;
using SwiftExport.Application.Services.OrderTracking.Sources;
using SwiftExport.Application.Services.OrderTracking.DemandDetail;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.DTOs.OrderTracking.DemandDetail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.OrderTracking.Overview;
using SwiftExport.Core.Interfaces.OrderTracking.Sources;
using SwiftExport.Core.Interfaces.OrderTracking.DemandDetail;
namespace SwiftExport.Application.Tests.OrderTracking;

/// <summary>验证新来源全量契约、主表输入校验和独立错误语义。</summary>
[TestClass]
public sealed class OrderTrackingServiceTests
{
    /// <summary>非法来源不能扩大为全部来源，服务拒绝路径不进入仓储。</summary>
    [TestMethod,DataRow("ZZ"),DataRow(""),DataRow("so"),DataRow(null)]
    public async Task InvalidSource_IsRejectedWithoutRepository(string? value)
    {
        var repo=new Repository();
        var result=await new OrderTrackingSourceService(Context(),repo).SearchSourcesAsync(new(){SourceTypes=[value!]});
        Assert.AreEqual(ServiceErrorCodes.Validation,result.Error.Code);Assert.AreEqual(0,repo.Calls);
        var child=await new OrderTrackingOverviewService(Context(),repo).GetSourceDemandsAsync(value!,Guid.NewGuid());
        Assert.AreEqual(ServiceErrorCodes.Validation,child.Error.Code);Assert.AreEqual(0,repo.Calls);
    }
    /// <summary>空来源组身份在SQL前拒绝，不退化为未限定查询。</summary>
    [TestMethod]
    public async Task EmptySourceGroup_IsRejected()
    {
        var repo=new Repository();var result=await new OrderTrackingOverviewService(Context(),repo).GetSourceDemandsAsync("SO",Guid.Empty);
        Assert.AreEqual(ServiceErrorCodes.Validation,result.Error.Code);Assert.AreEqual(0,repo.Calls);
    }
    /// <summary>未定义生命周期不得静默变为空列表。</summary>
    [TestMethod,DataRow(-1),DataRow(1),DataRow(999)]
    public async Task InvalidLifecycle_IsRejectedBeforeRead(int value)
    {
        var repo=new Repository();
        var result=await new OrderTrackingSourceService(Context(),repo).SearchSourcesAsync(new(){LifecycleStatuses=[(GrossDemandLifecycleStatusEnum)value]});
        Assert.AreEqual(ServiceErrorCodes.Validation,result.Error.Code);Assert.AreEqual(0,repo.Calls);
    }
    /// <summary>合法筛选原样交给仓储，业务日一次取值，请求不被归一化回写。</summary>
    [TestMethod]
    public async Task ValidQueries_DoNotMutateRequest()
    {
        var repo=new Repository();var service=new OrderTrackingSourceService(Context(),repo);
        foreach(var values in new List<string>?[]{["SO","CR","MD"],[],null})
        {
            var request=new OrderTrackingSourceSearchRequest{SourceTypes=values!,PageIndex=0,PageSize=999,CustomerCodes=[" 原文 "]};
            var before=JsonSerializer.Serialize(request);Assert.IsTrue((await service.SearchSourcesAsync(request)).Success);
            Assert.AreSame(request,repo.Request);Assert.AreEqual(before,JsonSerializer.Serialize(request));Assert.AreEqual(repo.BusinessDate.Date,repo.BusinessDate);
        }
    }
    /// <summary>倒置日期、空需求身份和未找到保持不同错误。</summary>
    [TestMethod]
    public async Task InvalidDateAndMissingIdentity_AreDistinct()
    {
        var repo=new Repository();
        var invalid=await new OrderTrackingSourceService(Context(),repo).SearchSourcesAsync(new(){RequiredDateFrom=new(2026,10,20),RequiredDateTo=new(2026,10,19)});
        Assert.AreEqual(ServiceErrorCodes.Validation,invalid.Error.Code);Assert.AreEqual(0,repo.Calls);
        var detail=new DemandTrackingHeaderService(Context(),repo);
        Assert.AreEqual(ServiceErrorCodes.Validation,(await detail.GetDemandHeaderAsync(Guid.Empty)).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.NotFound,(await detail.GetDemandHeaderAsync(Guid.NewGuid())).Error.Code);
    }
    /// <summary>技术失败不伪装成成功空数据，取消原样传播。</summary>
    [TestMethod]
    public async Task FailureAndCancellation_AreNotEmptySuccess()
    {
        var repo=new Repository{Failure=new InvalidOperationException("内部数据库信息")};
        var source=new OrderTrackingSourceService(Context(),repo);var child=new OrderTrackingOverviewService(Context(),repo);var detail=new DemandTrackingHeaderService(Context(),repo);
        var failed=await source.SearchSourcesAsync(new());Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable,failed.Error.Code);Assert.IsFalse(failed.Message.Contains("内部数据库信息",StringComparison.Ordinal));
        Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable,(await child.GetSourceDemandsAsync("SO",Guid.NewGuid())).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable,(await child.GetCustomerCodesAsync()).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable,(await detail.GetDemandHeaderAsync(Guid.NewGuid())).Error.Code);
        var token=new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(()=>source.SearchSourcesAsync(new(),token));
        await Assert.ThrowsAsync<OperationCanceledException>(()=>child.GetSourceDemandsAsync("SO",Guid.NewGuid(),token));
        await Assert.ThrowsAsync<OperationCanceledException>(()=>child.GetCustomerCodesAsync(token));
        await Assert.ThrowsAsync<OperationCanceledException>(()=>detail.GetDemandHeaderAsync(Guid.NewGuid(),token));
    }
    private static ServiceBaseContext Context()=>new(null!,NullLoggerFactory.Instance);
    /// <summary>只记录窄读取调用，不在替身中实现筛选或数量算法。</summary>
    private sealed class Repository:IOrderTrackingOverviewRepository,IOrderTrackingSourceRepository,IDemandTrackingHeaderRepository
    {
        internal int Calls;internal Exception? Failure;internal OrderTrackingSourceSearchRequest? Request;internal DateTime BusinessDate;
        /// <summary>记录来源参数及同次业务日。</summary>
        public Task<PagedResponseBase<OrderTrackingSourceGroupDto>> SearchSourcesAsync(OrderTrackingSourceSearchRequest request,DateTime day,CancellationToken cancellationToken=default)
        {Calls++;Request=request;BusinessDate=day;if(Failure is not null)throw Failure;return Task.FromResult(new PagedResponseBase<OrderTrackingSourceGroupDto>());}
        /// <summary>匹配接口替身只记录调用；具体匹配行为由专项仓储测试验证。</summary>
        public Task<List<Guid>> GetSourceMatchesAsync(string type,Guid id,OrderTrackingSourceMatchRequest request,DateTime day,CancellationToken cancellationToken=default)
        {Calls++;if(Failure is not null)throw Failure;return Task.FromResult(new List<Guid>());}
        /// <summary>只接收来源身份，不存在任何主表筛选输入。</summary>
        public Task<OrderTrackingSourceDemandsDto> GetSourceDemandsAsync(string type,Guid id,CancellationToken cancellationToken=default)
        {Calls++;if(Failure is not null)throw Failure;return Task.FromResult(new OrderTrackingSourceDemandsDto{SourceType=type,SourceGroupId=id});}
        public Task<List<string>> GetCustomerCodesAsync(CancellationToken cancellationToken=default)
        {Calls++;if(Failure is not null)throw Failure;return Task.FromResult(new List<string>());}
        public Task<DemandTrackingHeaderDto?> GetDemandHeaderAsync(Guid id,CancellationToken cancellationToken=default)
        {Calls++;if(Failure is not null)throw Failure;return Task.FromResult<DemandTrackingHeaderDto?>(null);}
    }
}
