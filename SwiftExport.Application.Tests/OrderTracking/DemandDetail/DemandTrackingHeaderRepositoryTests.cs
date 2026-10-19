using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.OrderTracking.DemandDetail;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Application.Tests.OrderTracking.DemandDetail;

/// <summary>验证基础明细完整快照和独立加载边界。</summary>
[TestClass]
public sealed class DemandTrackingHeaderRepositoryTests
{
    /// <summary>生命周期、审计、原始文本和 NULL 经真实映射，读取不校验下游或当前产品。</summary>
    [TestMethod]
    public async Task Detail_MapsAllFieldsWithoutDownstream()
    {
        using var f = new OrderTrackingReadFixture(true);
        var expected = new DemandTrackingHeaderDto { GrossDemandId = Guid.NewGuid(), Version = 9, SourceType = "SO", SourceUid = Guid.NewGuid(),
            SourceLineId = Guid.NewGuid(), SourceNo = "来源快照", SourceLineNumber = 3, SourceVersion = null, CustomerId = Guid.NewGuid(),
            CustomerCode = null!, CustomerName = null!, CustomerPo = null!, InvoiceNo = null!, ProductId = Guid.NewGuid(), IsNonStandard = false,
            Sku = " 原型号 ", CustomerSku = "客型", LinkSku = "中性", ImagePath = "图片", Description = "描述", Color = "颜色", Specification = "规格",
            DemandQty = 8.5m, Unit = null!, RequiredDate = null, LifecycleStatus = GrossDemandLifecycleStatusEnum.Closed, Remark = "备注",
            LifecycleChangedAt = new(2026, 10, 19), LifecycleChangedBy = "关闭人", LifecycleChangedReason = "原因", CreatedDate = null,
            CreatedBy = "建单人", UpdatedDate = new(2026, 10, 19, 15, 20, 0), UpdatedBy = "更新人" };
        f.Connection.Results.Enqueue(OrderTrackingReadFixture.Table(expected));
        using var cts = new CancellationTokenSource();
        var actual = await f.Detail.GetDemandHeaderAsync(expected.GrossDemandId, cts.Token);
        Assert.IsNotNull(actual);
        foreach (var property in typeof(DemandTrackingHeaderDto).GetProperties())
            Assert.AreEqual(property.GetValue(expected), property.GetValue(actual), property.Name);
        var c = f.Connection.Commands.Single();
        Assert.AreEqual(expected.GrossDemandId, c.Value("GrossDemandId")); Assert.AreEqual(cts.Token, f.Connection.Token);
        Assert.AreSame(f.Session.Transaction, c.Transaction); Assert.AreEqual(0, f.Connection.Commits + f.Connection.Rollbacks);
        foreach (var forbidden in new[] { "net_demand", "purchase_order", "packing_list", "public.product", "COALESCE", "FOR UPDATE" })
            Assert.IsFalse(c.Sql.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        foreach (var property in typeof(DemandTrackingHeaderDto).GetProperties())
            StringAssert.Contains(c.Sql, "AS \"" + property.Name + "\"");
    }

    /// <summary>空结果为不存在，取消原样传播，不把它们伪装成基础事实零值。</summary>
    [TestMethod]
    public async Task MissingAndCancellation_RemainDistinct()
    {
        using var f = new OrderTrackingReadFixture();
        f.Connection.Results.Enqueue(new System.Data.DataTable());
        Assert.IsNull(await f.Detail.GetDemandHeaderAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Detail.GetDemandHeaderAsync(Guid.NewGuid(), new CancellationToken(true)));
        Assert.HasCount(1, f.Connection.Commands);
    }
}
