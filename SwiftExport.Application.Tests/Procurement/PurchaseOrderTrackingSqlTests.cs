using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>在随机隔离 LocalDB 中核对采购整单状态、先筛后分页及独立采购追溯。</summary>
[TestClass, DoNotParallelize]
public sealed class PurchaseOrderTrackingSqlTests
{
    /// <summary>未反馈、明确零与部分货好分开筛选；空明细不会被误判为全部完成。</summary>
    [TestMethod]
    public async Task ReadyAndReceiptStatesFilterBeforePaging()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_SQL_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_SQL_TESTS=1 后运行随机隔离 LocalDB 测试。");
        await using var db = new IndependentProcurementSqlFixture();
        await db.InitializeAsync();
        var first = (await db.CreateAsync()).Data!;
        var second = (await db.CreateAsync()).Data!;
        var third = (await db.CreateAsync()).Data!;
        var empty = (await db.CreateAsync()).Data!;
        var secondLine = await db.ScalarAsync<Guid>("SELECT TOP (1) Uid FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@id", new { id = second.PurchaseOrderId });
        var thirdLine = await db.ScalarAsync<Guid>("SELECT TOP (1) Uid FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@id", new { id = third.PurchaseOrderId });
        await db.ExecuteAsync("""
            INSERT dbo.SupplierProgressReport(Uid,PurchaseOrderLineId,Sequence,ArrangementStatus,ReadyQty,SupplierReportedDate,CommandId)
            VALUES(NEWID(),@secondLine,1,0,0,SYSUTCDATETIME(),NEWID()),
                  (NEWID(),@thirdLine,1,0,4,SYSUTCDATETIME(),NEWID());
            UPDATE dbo.PurchaseOrderLine SET IsDeleted=1 WHERE PurchaseOrderId=@emptyId;
            """, new { secondLine, thirdLine, emptyId = empty.PurchaseOrderId });

        async Task<PurchaseOrderManagerListItemDto> Find(PurchaseReadyStatusEnum status)
        {
            var result = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>()
                .SearchAsync(new PurchaseOrderManagerSearchRequest { ReadyStatuses = [status], PageIndex = 1, PageSize = 1 }));
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(1, result.Data!.TotalCount);
            return result.Data.Items.Single();
        }
        Assert.AreEqual(first.PurchaseOrderId, (await Find(PurchaseReadyStatusEnum.NotReported)).PurchaseOrderId);
        Assert.AreEqual(second.PurchaseOrderId, (await Find(PurchaseReadyStatusEnum.NotReady)).PurchaseOrderId);
        Assert.AreEqual(third.PurchaseOrderId, (await Find(PurchaseReadyStatusEnum.PartiallyReady)).PurchaseOrderId);
        Assert.AreEqual(empty.PurchaseOrderId, (await Find(PurchaseReadyStatusEnum.NoLines)).PurchaseOrderId);

        await db.ExecuteAsync("""
            INSERT dbo.InboundDocument(Uid,DocumentNo,DocumentType,CreationMode,LifecycleStatus,PostingStatus,
                WarehouseSiteId,ReceiptDate)
            VALUES(@inboundId,N'TEST-RECEIPT-TRACK',10,0,0,1,@site,SYSUTCDATETIME());
            INSERT dbo.InboundDocumentLine(Uid,InboundDocumentId,LineNumber,ProductId,SkuSnapshot,UnitSnapshot,Quantity,SourceLineId)
            VALUES(NEWID(),@inboundId,1,@product,N'TEST-FG',N'PCS',3,@thirdLine);
            """, new { inboundId = Guid.NewGuid(), site = db.ReceiptSiteId, product = db.ProductId, thirdLine });

        var receipt = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>()
            .SearchAsync(new PurchaseOrderManagerSearchRequest { ReceiptStatuses = [PurchaseReceiptStatusEnum.NotReceived], PageSize = 2 }));
        Assert.IsTrue(receipt.Success, receipt.Message);
        Assert.AreEqual(2, receipt.Data!.TotalCount);
        Assert.AreEqual(2, receipt.Data.Items.Count);
        var partialReceipt = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>()
            .SearchAsync(new PurchaseOrderManagerSearchRequest { ReceiptStatuses = [PurchaseReceiptStatusEnum.PartiallyReceived] }));
        Assert.IsTrue(partialReceipt.Success, partialReceipt.Message);
        Assert.AreEqual(third.PurchaseOrderId, partialReceipt.Data!.Items.Single().PurchaseOrderId);
    }

    /// <summary>独立采购无需求 Link 时仍返回采购行；混合单位不显示跨行数量合计。</summary>
    [TestMethod]
    public async Task IndependentTraceAndMixedUnitsRemainExplicit()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_SQL_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_SQL_TESTS=1 后运行随机隔离 LocalDB 测试。");
        await using var db = new IndependentProcurementSqlFixture();
        await db.InitializeAsync();
        var command = db.CreateCommand();
        command.Lines.Add(new() { ProductId = db.ComponentId, Quantity = 5m, UnitPrice = 2m, TaxPrice = 2.5m });
        var created = await db.CreateAsync(command);
        Assert.IsTrue(created.Success, created.Message);
        var uid = created.Data!.PurchaseOrderId;
        await db.ExecuteAsync("UPDATE dbo.PurchaseOrderLine SET UnitSnapshot=N'BOX' WHERE PurchaseOrderId=@uid AND LineNumber=2", new { uid });
        var list = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>()
            .SearchAsync(new PurchaseOrderManagerSearchRequest { Keyword = created.Data.OrderNo }));
        Assert.IsTrue(list.Success, list.Message);
        var row = list.Data!.Items.Single();
        Assert.AreEqual(2, row.LineCount);
        Assert.IsNull(row.TotalQty);
        Assert.IsNull(row.ReceiptProgress);
        var trace = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderTraceService>().GetAsync(uid));
        Assert.IsTrue(trace.Success, trace.Message);
        Assert.AreEqual(2, trace.Data!.OriginKind);
        Assert.AreEqual(2, trace.Data.Lines.Count);
        Assert.IsTrue(trace.Data.Lines.All(line => line.Links.Count == 0));
        await db.ExecuteAsync("""
            INSERT dbo.InboundDocument(Uid,DocumentNo,DocumentType,CreationMode,LifecycleStatus,PostingStatus,WarehouseSiteId,ReceiptDate)
            VALUES(NEWID(),N'IR-TRACE-UNVERIFIED',10,0,0,8,@site,SYSUTCDATETIME());
            """, new { site = db.ReceiptSiteId });
        var unrelatedReceipt = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderTraceService>().GetAsync(uid));
        Assert.IsTrue(unrelatedReceipt.Success, unrelatedReceipt.Message);
        var purchaseLineId = await db.ScalarAsync<Guid>(
            "SELECT TOP (1) Uid FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@uid ORDER BY LineNumber", new { uid });
        await db.ExecuteAsync("""
            DECLARE @receipt uniqueidentifier=NEWID();
            INSERT dbo.InboundDocument(Uid,DocumentNo,DocumentType,CreationMode,LifecycleStatus,PostingStatus,WarehouseSiteId,ReceiptDate)
            VALUES(@receipt,N'IR-TRACE-RELATED',10,0,0,8,@site,SYSUTCDATETIME());
            INSERT dbo.InboundDocumentLine(Uid,InboundDocumentId,LineNumber,ProductId,SkuSnapshot,UnitSnapshot,Quantity,SourceLineId)
            VALUES(NEWID(),@receipt,1,@productId,N'TEST-FG',N'PCS',1,@purchaseLineId);
            """, new { site = db.ReceiptSiteId, productId = db.ProductId, purchaseLineId });
        var unknownReceipt = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderTraceService>().GetAsync(uid));
        Assert.IsFalse(unknownReceipt.Success);
        StringAssert.Contains(unknownReceipt.Message, "收货量未知");
    }

    /// <summary>北京时间边界以采购行计划交期判定；未知货好量作为风险命中，已货好行退出提醒。</summary>
    [TestMethod]
    public async Task DueViewsUseBeijingDateAndUnconfirmedReadyRisk()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_SQL_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_SQL_TESTS=1 后运行随机隔离 LocalDB 测试。");
        await using var db = new IndependentProcurementSqlFixture();
        await db.InitializeAsync();
        var late = (await db.CreateAsync()).Data!;
        var soon = (await db.CreateAsync()).Data!;
        var later = (await db.CreateAsync()).Data!;
        var today = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "China Standard Time").Date;
        await db.ExecuteAsync("""
            UPDATE dbo.PurchaseOrderHead SET ConfirmationStatus=1 WHERE Uid IN @ids;
            UPDATE dbo.PurchaseOrderLine SET PlannedDeliveryDate=@lateDate WHERE PurchaseOrderId=@lateId;
            UPDATE dbo.PurchaseOrderLine SET PlannedDeliveryDate=@soonDate WHERE PurchaseOrderId=@soonId;
            UPDATE dbo.PurchaseOrderLine SET PlannedDeliveryDate=@laterDate WHERE PurchaseOrderId=@laterId;
            """, new { ids = new[] { late.PurchaseOrderId, soon.PurchaseOrderId, later.PurchaseOrderId },
                lateId = late.PurchaseOrderId, soonId = soon.PurchaseOrderId, laterId = later.PurchaseOrderId,
                lateDate = today.AddDays(-1), soonDate = today.AddDays(7), laterDate = today.AddDays(8) });

        async Task<PurchaseOrderManagerListItemDto> Find(PurchaseDueViewEnum view)
        {
            var result = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>()
                .SearchAsync(new PurchaseOrderManagerSearchRequest { DueView = view }));
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(1, result.Data!.TotalCount);
            return result.Data.Items.Single();
        }
        var overdue = await Find(PurchaseDueViewEnum.OverdueNotReady);
        Assert.AreEqual(late.PurchaseOrderId, overdue.PurchaseOrderId);
        Assert.IsTrue(overdue.HasUnconfirmedReadyRisk);
        Assert.AreEqual(soon.PurchaseOrderId, (await Find(PurchaseDueViewEnum.DueSoonNotReady)).PurchaseOrderId);

        var soonLine = await db.ScalarAsync<Guid>("SELECT TOP (1) Uid FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@id", new { id = soon.PurchaseOrderId });
        await db.ExecuteAsync("""
            INSERT dbo.SupplierProgressReport(Uid,PurchaseOrderLineId,Sequence,ArrangementStatus,ReadyQty,SupplierReportedDate,CommandId)
            VALUES(NEWID(),@soonLine,1,0,10,SYSUTCDATETIME(),NEWID());
            """, new { soonLine });
        var noSoon = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>()
            .SearchAsync(new PurchaseOrderManagerSearchRequest { DueView = PurchaseDueViewEnum.DueSoonNotReady }));
        Assert.IsTrue(noSoon.Success, noSoon.Message);
        Assert.AreEqual(0, noSoon.Data!.TotalCount);
    }
}
