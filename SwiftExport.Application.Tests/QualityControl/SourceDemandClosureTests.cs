using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;

namespace SwiftExport.Application.Tests.QualityControl;

/// <summary>交付事实未接入期间，查询不得伪造零，结案与额度核验不得伪造完成证据。</summary>
[TestClass]
public sealed class SourceDeliveryClosureTests
{
    /// <summary>只读边界返回稳定不可用错误及空数据，不冒充成功空列表。</summary>
    [TestMethod]
    public async Task QueriesExplicitlyReportUnavailable()
    {
        var service = new SourceDeliveryService();
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
        var service = new SourceDeliveryService();
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
        var service = new SourceDeliveryService();
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetAsync(Guid.NewGuid(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SearchAsync(new SourceDemandDeliverySearchRequest(), token));
    }
}
