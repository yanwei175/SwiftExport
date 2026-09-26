using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Interfaces.Shipping.PackingLists;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.QualityControl;

/// <summary>验证统一来源查询、额度复核、结案失败关闭和来源退出边界。</summary>
[TestClass]
public sealed class SourceDeliveryClosureTests
{
    /// <summary>只读边界返回仓储提供的可证明交付数据。</summary>
    [TestMethod]
    public async Task QueriesReturnProvenDeliveryProjection()
    {
        var service = CreateService(out _, out var query, withTransaction: false);
        var item = new SourceDemandDeliveryItemDto { GrossDemandId = Guid.NewGuid(), RemainingQty = 3m };
        query.SearchResult = new PagedResponseBase<SourceDemandDeliveryItemDto>
            { Items = [item], TotalCount = 1, PageIndex = 1, PageSize = 20 };
        query.Detail = item;
        var search = await service.SearchAsync(new SourceDemandDeliverySearchRequest());
        var detail = await service.GetAsync(item.GrossDemandId);
        Assert.IsTrue(search.Success);
        Assert.IsTrue(detail.Success);
        Assert.AreEqual(item.GrossDemandId, search.Data.Items.Single().GrossDemandId);
        Assert.AreEqual(3m, detail.Data.RemainingQty);
    }

    /// <summary>销售与售后、空声明与正常声明均不能绕过未接入阻断。</summary>
    [TestMethod]
    public async Task ClosureAndQuotaCannotSucceedWithoutDeliveryEvidence()
    {
        var service = CreateService(out _, out _, withTransaction: true);
        foreach (var sourceType in new[] { "SO", "CR" })
        foreach (var expected in new[] { new Dictionary<Guid, decimal>(), new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 10m } })
        {
            var error = await Assert.ThrowsAsync<SourceDeliveryException>(
                () => service.VerifyClosureAsync(sourceType, Guid.NewGuid(), expected));
            Assert.AreEqual("DELIVERY_QUERY_UNAVAILABLE", error.Code);
        }
        var empty = await service.VerifyForUpdateAsync([]);
        Assert.AreEqual(0, empty.Count);
    }

    /// <summary>取消请求优先结束，不吞掉调用方取消信号。</summary>
    [TestMethod]
    public async Task CancellationIsPreserved()
    {
        var service = CreateService(out _, out _, withTransaction: false);
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetAsync(Guid.NewGuid(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SearchAsync(new SourceDemandDeliverySearchRequest(), token));
    }

    /// <summary>额度复核聚合同一来源的多条声明，并校验来源版本与剩余数量。</summary>
    [TestMethod]
    public async Task QuotaVerificationAggregatesClaimsAndChecksVersions()
    {
        var service = CreateService(out _, out var query, withTransaction: true);
        var sourceId = Guid.NewGuid();
        query.Detail = new SourceDemandDeliveryItemDto
        {
            GrossDemandId = sourceId,
            DemandVersion = 3,
            SourceVersion = 7,
            CustomerId = Guid.NewGuid(),
            IsActive = true,
            RemainingQty = 10m
        };
        var claims = new[]
        {
            new SourceDeliveryClaim { GrossDemandId = sourceId, ExpectedDemandVersion = 3, ExpectedSourceVersion = 7, Quantity = 4m },
            new SourceDeliveryClaim { GrossDemandId = sourceId, ExpectedDemandVersion = 3, ExpectedSourceVersion = 7, Quantity = 6m }
        };
        var result = await service.VerifyForUpdateAsync(claims);
        Assert.AreEqual(10m, result[sourceId].RemainingQty);

        claims[1].Quantity = 6.0001m;
        var exceeded = await Assert.ThrowsAsync<SourceDeliveryException>(() => service.VerifyForUpdateAsync(claims));
        Assert.AreEqual("SOURCE_DELIVERY_EXCEEDED", exceeded.Code);

        claims[1].Quantity = 6m;
        claims[0].ExpectedDemandVersion = 2;
        var conflict = await Assert.ThrowsAsync<SourceDeliveryException>(() => service.VerifyForUpdateAsync(claims));
        Assert.AreEqual("SOURCE_DELIVERY_VERSION_CONFLICT", conflict.Code);
    }

    /// <summary>有效草稿、正式 PackingList 或正式客户出库任一存在都阻断来源退出。</summary>
    [TestMethod]
    public async Task SourceExitRejectsEveryEffectiveDeliveryKind()
    {
        var service = CreateService(out var port, out _, withTransaction: true);
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
        var service = CreateService(out var port, out _, withTransaction: true);
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
        var service = CreateService(out var port, out _, withTransaction: true);
        port.SchemaReady = false;
        var error = await Assert.ThrowsAsync<SourceDeliveryException>(() => service.VerifySourceExitAsync(
            "SO", Guid.NewGuid(), [Guid.NewGuid()]));
        Assert.AreEqual("DELIVERY_QUERY_UNAVAILABLE", error.Code);
    }

    private static SourceDeliveryService CreateService(
        out PackingListPort port,
        out SourceQueryPort queryPort,
        bool withTransaction)
    {
        var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
        if (withTransaction)
            session.Transaction = DispatchProxy.Create<IDbTransaction, TransactionPort>();
        var repository = DispatchProxy.Create<IPackingListSourceExitRepository, PackingListPort>();
        port = (PackingListPort)(object)repository;
        var query = DispatchProxy.Create<ISourceDeliveryQueryRepository, SourceQueryPort>();
        queryPort = (SourceQueryPort)(object)query;
        return new SourceDeliveryService(
            new ServiceBaseContext(session, NullLoggerFactory.Instance), query, repository);
    }

    /// <summary>提供统一来源查询所需的内存结果。</summary>
    public class SourceQueryPort : DispatchProxy
    {
        public bool SchemaReady { get; set; } = true;
        public PagedResponseBase<SourceDemandDeliveryItemDto> SearchResult { get; set; } = new();
        public SourceDemandDeliveryItemDto? Detail { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(ISourceDeliveryQueryRepository.IsSchemaReadyAsync) => Task.FromResult(SchemaReady),
            nameof(ISourceDeliveryQueryRepository.SearchAsync) => Task.FromResult(SearchResult),
            nameof(ISourceDeliveryQueryRepository.GetAsync) => Task.FromResult(Detail),
            nameof(ISourceDeliveryQueryRepository.GetForUpdateAsync) => Task.FromResult<IReadOnlyList<SourceDemandDeliveryItemDto>>(
                Detail is null ? [] : [Detail]),
            _ => throw new NotSupportedException(targetMethod?.Name)
        };
    }

    /// <summary>提供来源退出核验所需的最小 PackingList 仓储行为。</summary>
    public class PackingListPort : DispatchProxy
    {
        public bool SchemaReady { get; set; } = true;
        public SourceDeliveryExitAssessment Assessment { get; set; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IPackingListSourceExitRepository.IsSchemaReadyAsync) => Task.FromResult(SchemaReady),
            nameof(IPackingListSourceExitRepository.AssessSourceExitForUpdateAsync) => Task.FromResult(Assessment),
            _ => throw new NotSupportedException(targetMethod?.Name)
        };
    }

    /// <summary>只提供非空事务标记。</summary>
    public class TransactionPort : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
