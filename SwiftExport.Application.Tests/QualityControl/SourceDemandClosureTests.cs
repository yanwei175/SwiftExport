using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.QualityControl;

/// <summary>验证来源退出窄核验以及尚未接入的查询、结案和额度能力均按各自边界失败关闭。</summary>
[TestClass]
public sealed class SourceDeliveryClosureTests
{
    /// <summary>只读边界返回稳定不可用错误及空数据，不冒充成功空列表。</summary>
    [TestMethod]
    public async Task QueriesExplicitlyReportUnavailable()
    {
        var service = CreateService(out _, withTransaction: false);
        var search = await service.SearchAsync(new SourceDemandDeliverySearchRequest());
        var detail = await service.GetAsync(Guid.NewGuid());
        Assert.IsFalse(search.Success);
        Assert.IsFalse(detail.Success);
        Assert.IsNull(search.Data);
        Assert.IsNull(detail.Data);
        Assert.AreEqual("DELIVERY_QUERY_UNAVAILABLE", search.Error.Code);
        Assert.AreEqual("DELIVERY_QUERY_UNAVAILABLE", detail.Error.Code);
    }

    /// <summary>销售与售后、空声明与正常声明均不能绕过未接入阻断。</summary>
    [TestMethod]
    public async Task ClosureAndQuotaCannotSucceedWithoutDeliveryEvidence()
    {
        var service = CreateService(out _, withTransaction: false);
        foreach (var sourceType in new[] { "SO", "CR" })
        foreach (var expected in new[] { new Dictionary<Guid, decimal>(), new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 10m } })
        {
            var error = await Assert.ThrowsAsync<SourceDeliveryException>(
                () => service.VerifyClosureAsync(sourceType, Guid.NewGuid(), expected));
            Assert.AreEqual("DELIVERY_QUERY_UNAVAILABLE", error.Code);
        }
        var quotaError = await Assert.ThrowsAsync<SourceDeliveryException>(
            () => service.VerifyForUpdateAsync([], false));
        Assert.AreEqual("DELIVERY_QUERY_UNAVAILABLE", quotaError.Code);
    }

    /// <summary>取消请求优先结束，不吞掉调用方取消信号。</summary>
    [TestMethod]
    public async Task CancellationIsPreserved()
    {
        var service = CreateService(out _, withTransaction: false);
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetAsync(Guid.NewGuid(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SearchAsync(new SourceDemandDeliverySearchRequest(), token));
    }

    /// <summary>有效草稿、正式 PackingList 或正式客户出库任一存在都阻断来源退出。</summary>
    [TestMethod]
    public async Task SourceExitRejectsEveryEffectiveDeliveryKind()
    {
        var service = CreateService(out var port, withTransaction: true);
        foreach (var assessment in new[]
                 {
                     new SourceDeliveryExitAssessment { ActiveDraftCount = 1 },
                     new SourceDeliveryExitAssessment { PostedPackingListCount = 1 },
                     new SourceDeliveryExitAssessment { PostedCustomerOutboundCount = 1 }
                 })
        {
            port.Assessment = assessment;
            var error = await Assert.ThrowsAsync<SourceDeliveryException>(() => service.VerifySourceExitAsync(
                "SO", Guid.NewGuid(), [Guid.NewGuid()]));
            Assert.AreEqual("SOURCE_DELIVERY_EXISTS", error.Code);
        }
    }

    /// <summary>异常历史失败关闭；全部交付反向后允许来源退出。</summary>
    [TestMethod]
    public async Task SourceExitRejectsInvalidHistoryAndAllowsSafeHistory()
    {
        var service = CreateService(out var port, withTransaction: true);
        port.Assessment = new SourceDeliveryExitAssessment { InvalidHistoryCount = 1 };
        var error = await Assert.ThrowsAsync<SourceDeliveryException>(() => service.VerifySourceExitAsync(
            "SO", Guid.NewGuid(), [Guid.NewGuid()]));
        Assert.AreEqual("SOURCE_DELIVERY_HISTORY_INVALID", error.Code);

        port.Assessment = new SourceDeliveryExitAssessment();
        await service.VerifySourceExitAsync("SO", Guid.NewGuid(), [Guid.NewGuid()]);
    }

    /// <summary>数据库结构不可用时不得把未知交付事实当作安全。</summary>
    [TestMethod]
    public async Task SourceExitFailsClosedWhenSchemaIsUnavailable()
    {
        var service = CreateService(out var port, withTransaction: true);
        port.SchemaReady = false;
        var error = await Assert.ThrowsAsync<SourceDeliveryException>(() => service.VerifySourceExitAsync(
            "SO", Guid.NewGuid(), [Guid.NewGuid()]));
        Assert.AreEqual("DELIVERY_QUERY_UNAVAILABLE", error.Code);
    }

    private static SourceDeliveryService CreateService(out PackingListPort port, bool withTransaction)
    {
        var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
        if (withTransaction)
            session.Transaction = DispatchProxy.Create<IDbTransaction, TransactionPort>();
        var repository = DispatchProxy.Create<IPackingListRepository, PackingListPort>();
        port = (PackingListPort)(object)repository;
        return new SourceDeliveryService(
            new ServiceBaseContext(session, NullLoggerFactory.Instance), repository);
    }

    /// <summary>提供来源退出核验所需的最小 PackingList 仓储行为。</summary>
    public class PackingListPort : DispatchProxy
    {
        public bool SchemaReady { get; set; } = true;
        public SourceDeliveryExitAssessment Assessment { get; set; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IPackingListRepository.IsSchemaReadyAsync) => Task.FromResult(SchemaReady),
            nameof(IPackingListRepository.AssessSourceExitForUpdateAsync) => Task.FromResult(Assessment),
            _ => throw new NotSupportedException(targetMethod?.Name)
        };
    }

    /// <summary>只提供非空事务标记。</summary>
    public class TransactionPort : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
