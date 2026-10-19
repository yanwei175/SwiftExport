using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.OrderTracking.Overview;
using SwiftExport.Application.Services.OrderTracking.DemandDetail;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.OrderTracking.DemandDetail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.OrderTracking.Overview;
using SwiftExport.Core.Interfaces.OrderTracking.DemandDetail;

namespace SwiftExport.Application.Tests.OrderTracking;

/// <summary>验证只读应用边界的输入校验、取消及错误分类。</summary>
[TestClass]
public sealed class OrderTrackingServiceTests
{
    /// <summary>来源组类型和身份必须成对，错误选择不能退化为全部需求读取。</summary>
    [TestMethod]
    [DataRow("missing-type")]
    [DataRow("missing-id")]
    [DataRow("empty-id")]
    [DataRow("unknown-type")]
    public async Task SourceSelection_RejectsIncompletePair(string mode)
    {
        var repo=new Repository();
        var request=new OrderTrackingSearchRequest{SourceGroupType="SO",SourceGroupId=Guid.NewGuid()};
        if(mode=="missing-type")request.SourceGroupType=null!;
        if(mode=="missing-id")request.SourceGroupId=null;
        if(mode=="empty-id")request.SourceGroupId=Guid.Empty;
        if(mode=="unknown-type")request.SourceGroupType="ZZ";
        var result=await new OrderTrackingOverviewService(Context(),repo).SearchAsync(request);
        Assert.AreEqual(ServiceErrorCodes.Validation,result.Error.Code);Assert.AreEqual(0,repo.Calls);
    }
    /// <summary>未定义生命周期不能静默降为成功空列表，失败路径不进入仓储。</summary>
    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    [DataRow(999)]
    public async Task InvalidLifecycle_IsRejectedBeforeRead(int value)
    {
        var repo = new Repository();
        var result = await new OrderTrackingOverviewService(Context(), repo).SearchAsync(new()
        {
            LifecycleStatuses = [(SwiftExport.Contracts.SysEnums.GrossDemandLifecycleStatusEnum)value]
        });
        Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        Assert.AreEqual("LifecycleStatuses", result.Error.Field);
        Assert.AreEqual(0, repo.Calls);
    }
    /// <summary>未知、空白与非规范来源在执行 SQL 前拒绝，不扩大为全部来源。</summary>
    [TestMethod]
    [DataRow("ZZ")]
    [DataRow("")]
    [DataRow("so")]
    [DataRow(null)]
    public async Task InvalidSource_IsRejectedWithoutRepository(string? source)
    {
        var repo = new Repository();
        var service = new OrderTrackingOverviewService(Context(), repo);
        var result = await service.SearchAsync(new() { SourceTypes = [source!] });
        Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        Assert.AreEqual(0, repo.Calls);
    }

    /// <summary>合法 SO/CR/MD、空集合及未传集合按契约交给仓储，不修改请求或快照。</summary>
    [TestMethod]
    public async Task ValidQueries_DoNotMutateRequest()
    {
        var repo = new Repository(); var service = new OrderTrackingOverviewService(Context(), repo);
        foreach (var sources in new List<string>?[] { ["SO", "CR", "MD"], [], null })
        {
            var request = new OrderTrackingSearchRequest { SourceTypes = sources!, PageIndex = 0, PageSize = 999, CustomerCodes = [" 原文 "] };
            var before = JsonSerializer.Serialize(request);
            var result = await service.SearchAsync(request);
            Assert.IsTrue(result.Success); Assert.AreSame(request, repo.Request);
            Assert.AreEqual(before, JsonSerializer.Serialize(request));
        }
    }

    /// <summary>倒置日期、空身份及不存在分别返回字段验证或 NOT_FOUND。</summary>
    [TestMethod]
    public async Task InvalidDateAndMissingIdentity_AreDistinct()
    {
        var repo = new Repository(); var overview = new OrderTrackingOverviewService(Context(), repo);
        var invalid = await overview.SearchAsync(new() { RequiredDateFrom = new(2026, 10, 20), RequiredDateTo = new(2026, 10, 19) });
        Assert.AreEqual(ServiceErrorCodes.Validation, invalid.Error.Code); Assert.AreEqual(0, repo.Calls);
        var detail = new DemandTrackingHeaderService(Context(), repo);
        Assert.AreEqual(ServiceErrorCodes.Validation, (await detail.GetDemandHeaderAsync(Guid.Empty)).Error.Code);
        Assert.AreEqual(0, repo.Calls);
        Assert.AreEqual(ServiceErrorCodes.NotFound, (await detail.GetDemandHeaderAsync(Guid.NewGuid())).Error.Code);
    }

    /// <summary>数据库失败不返回成功空集合，请求取消保持取消语义。</summary>
    [TestMethod]
    public async Task FailureAndCancellation_AreNotEmptySuccess()
    {
        var repo = new Repository { Failure = new InvalidOperationException("内部数据库信息") };
        var overview = new OrderTrackingOverviewService(Context(), repo); var detail = new DemandTrackingHeaderService(Context(), repo);
        var failed = await overview.SearchAsync(new());
        Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable, failed.Error.Code);
        Assert.IsFalse(failed.Message.Contains("内部数据库信息", StringComparison.Ordinal));
        Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable, (await overview.GetCustomerCodesAsync()).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable, (await detail.GetDemandHeaderAsync(Guid.NewGuid())).Error.Code);
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => overview.SearchAsync(new(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => overview.GetCustomerCodesAsync(token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => detail.GetDemandHeaderAsync(Guid.NewGuid(), token));
    }

    private static ServiceBaseContext Context() => new(null!, NullLoggerFactory.Instance);

    /// <summary>只记录调用与故障，不模拟仓储筛选算法。</summary>
    private sealed class Repository : IOrderTrackingOverviewRepository, IDemandTrackingHeaderRepository
    {
        internal int Calls;
        internal OrderTrackingSearchRequest? Request;
        internal Exception? Failure;
        /// <summary>记录总览调用，按用例指定抛错，不执行筛选。</summary>
        public Task<PagedResponseBase<OrderTrackingListItemDto>> SearchAsync(OrderTrackingSearchRequest request, CancellationToken cancellationToken = default)
        { Calls++; Request = request; if (Failure is not null) throw Failure; return Task.FromResult(new PagedResponseBase<OrderTrackingListItemDto>()); }
        /// <summary>模拟成功候选或读取故障，检查服务错误分类。</summary>
        public Task<List<string>> GetCustomerCodesAsync(CancellationToken cancellationToken = default)
        { Calls++; if (Failure is not null) throw Failure; return Task.FromResult(new List<string>()); }
        /// <summary>模拟缺失明细或技术故障，避免把不存在与读取失败混为一谈。</summary>
        public Task<DemandTrackingHeaderDto?> GetDemandHeaderAsync(Guid grossDemandId, CancellationToken cancellationToken = default)
        { Calls++; if (Failure is not null) throw Failure; return Task.FromResult<DemandTrackingHeaderDto?>(null); }
    }
}
