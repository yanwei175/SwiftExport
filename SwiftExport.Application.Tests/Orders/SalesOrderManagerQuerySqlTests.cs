using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.SalesOrders.Query;
using SwiftExport.Application.Services.DemandPlanning.Sources.Policies;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.SalesOrders.Query;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>在随机隔离 LocalDB 中验证销售管理查询的真实 SQL、交付汇总及快捷视图。</summary>
[TestClass, DoNotParallelize]
public sealed class SalesOrderManagerQuerySqlTests
{
    /// <summary>草稿零交付、已确认部分交付及反向变化共享同一行级计算口径。</summary>
    [TestMethod]
    public async Task DraftPostedAndReversedDelivery_KeepListDetailAndFiltersConsistent()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_SQL_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_SQL_TESTS=1 后运行隔离 LocalDB 销售查询测试。");
        await using var db = new IndependentProcurementSqlFixture();
        await db.InitializeAsync();
        var draft = Guid.NewGuid(); var draftLine = Guid.NewGuid();
        var confirmed = Guid.NewGuid(); var confirmedLine = Guid.NewGuid();
        var demand = Guid.NewGuid(); var packing = Guid.NewGuid(); var packingLine = Guid.NewGuid();
        var fingerprint = SourceDemandBasisFingerprintFactory.Create(new GrossDemand
        {
            CustomerId = db.CustomerId, IsNonStandard = true, SkuSnapshot = "CONF-SKU",
            CustomerSkuSnapshot = "CUSTOM-CONF", UnitSnapshot = "PCS", DemandQty = 10,
            RequiredDate = new DateTime(2026, 10, 2)
        });
        await db.ExecuteAsync("""
            INSERT dbo.SalesOrderHeader(Uid,DocumentStatus,ConfirmationStatus,OrderNo,InvoiceNo,CustomerId,CustomerCode,LeadTimeDays,OrderDate,DeliveryDate)
            VALUES(@draft,0,0,N'SO-DRAFT',N'INV-DRAFT',@customerId,N'TEST-C',7,'2026-09-01','2026-10-01'),
                  (@confirmed,0,1,N'SO-CONFIRMED',N'INV-CONFIRMED',@customerId,N'TEST-C',7,'2026-09-02','2026-10-02');
            INSERT dbo.SalesOrderLine(Uid,LineNumber,SalesOrderId,OrderNo,IsNonStandard,ProductId,Sku,CustomerSku,Qty,UnitSnapshot,ExpectedDeliveryDate)
            VALUES(@draftLine,1,@draft,N'SO-DRAFT',1,NULL,N'DRAFT-SKU',N'CUSTOM-DRAFT',10,N'PCS','2026-10-01'),
                  (@confirmedLine,1,@confirmed,N'SO-CONFIRMED',1,NULL,N'CONF-SKU',N'CUSTOM-CONF',10,N'PCS','2026-10-02');
            INSERT dbo.GrossDemand(Uid,LifecycleStatus,SourceType,SourceUid,SourceNo,SourceLineId,SourceLineNumber,SourceVersion,DemandBasisFingerprint,
                                    CustomerId,IsNonStandard,SkuSnapshot,CustomerSkuSnapshot,UnitSnapshot,DemandQty,RequiredDate)
            VALUES(@demand,0,N'SO',@confirmed,N'SO-CONFIRMED',@confirmedLine,1,1,@fingerprint,@customerId,1,N'CONF-SKU',N'CUSTOM-CONF',N'PCS',10,'2026-10-02');
            INSERT dbo.PackingListHead(Uid,PackingListNo,CustomerId,InvoiceDate,LifecycleStatus,PostingStatus,PostingDate,PostedDate,CreateCommandId)
            VALUES(@packing,N'PL-TEST',@customerId,'2026-09-02',0,1,'2026-09-02','2026-09-02',NEWID());
            INSERT dbo.PackingListLine(Uid,PackingListHeadId,LineNumber,GrossDemandId,IsNonStandardSnapshot,SourceTypeSnapshot,SourceDocumentNoSnapshot,
                                         SourceLineNumberSnapshot,SkuSnapshot,UnitSnapshot,Qty)
            VALUES(@packingLine,@packing,1,@demand,1,N'SO',N'SO-CONFIRMED',1,N'CONF-SKU',N'PCS',4);
            """, new { draft, draftLine, confirmed, confirmedLine, demand, packing, packingLine, fingerprint, customerId = db.CustomerId });

        async Task<SalesOrderManagerListItemDto> Single(SalesOrderManagerSearchRequest request)
        {
            var result = await db.InScopeAsync(p => p.GetRequiredService<ISalesOrderQueryService>().SearchAsync(request));
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(1, result.Data!.TotalCount);
            return result.Data.Items.Single();
        }
        var draftItem = await Single(new() { QuickView = SalesOrderManagerQuickView.PendingConfirmation });
        Assert.AreEqual(0m, draftItem.TotalShippedQty);
        Assert.AreEqual(10m, draftItem.TotalUnshippedQty);
        var draftFulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(draft));
        Assert.AreEqual(0m, draftFulfillment.Data!.Lines.Single().ShippedQty);
        Assert.AreEqual(10m, draftFulfillment.Data.Lines.Single().UnshippedQty);
        Assert.AreEqual("待确认", draftFulfillment.Data.Lines.Single().SourceState);
        Assert.AreEqual("未关联", draftFulfillment.Data.Lines.Single().PurchaseState);
        var item = await Single(new() { QuickView = SalesOrderManagerQuickView.PartiallyShipped });
        Assert.AreEqual(4m, item.TotalShippedQty);
        Assert.AreEqual(6m, item.TotalUnshippedQty);
        Assert.AreEqual(SalesOrderFulfillmentStatusEnum.PartiallyShipped, item.FulfillmentStatus);
        Assert.AreEqual("INV-CONFIRMED", item.InvoiceNo);
        var detail = await db.InScopeAsync(p => p.GetRequiredService<ISalesOrderQueryService>().GetAsync(confirmed));
        Assert.IsTrue(detail.Success, detail.Message);
        Assert.AreEqual(4m, detail.Data!.Lines.Single().ShippedQty);
        var fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.IsTrue(fulfillment.Success, fulfillment.Message);
        Assert.AreEqual(4m, fulfillment.Data!.Lines.Single().ShippedQty);
        Assert.AreEqual(6m, fulfillment.Data.Lines.Single().UnshippedQty);
        Assert.AreEqual(demand, fulfillment.Data.Lines.Single().GrossDemandId);
        Assert.AreEqual(1, fulfillment.Data.Lines.Single().Deliveries.Count);
        var net = Guid.NewGuid(); var otherGross = Guid.NewGuid(); var otherNet = Guid.NewGuid();
        var purchase = Guid.NewGuid(); var purchaseLine = Guid.NewGuid();
        var independent = Guid.NewGuid(); var independentLine = Guid.NewGuid();
        var ownLink = Guid.NewGuid(); var sharedLink = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT dbo.NetDemand(Uid,GrossDemandId,SourceDemandVersionSnapshot,SourceVersionSnapshot,SourceDemandBasisFingerprintSnapshot,
                DecompositionRevision,LineNumber,LifecycleStatus,RootNetDemandId,DeliveryMode,SupplyTypeSnapshot,SkuSnapshot,GrossQty,NetQty)
            VALUES(@net,@demand,(SELECT Version FROM dbo.GrossDemand WHERE Uid=@demand),1,@fingerprint,
                1,1,0,@net,0,N'采购',N'CONF-SKU',10,10);
            INSERT dbo.GrossDemand(Uid,LifecycleStatus,SourceType,SourceUid,SourceNo,SourceLineId,IsNonStandard,SkuSnapshot,DemandQty)
            VALUES(@otherGross,0,N'Manual',NEWID(),N'OTHER',NEWID(),1,N'OTHER-SKU',2);
            INSERT dbo.NetDemand(Uid,GrossDemandId,DecompositionRevision,LineNumber,LifecycleStatus,RootNetDemandId,DeliveryMode,SupplyTypeSnapshot,SkuSnapshot,GrossQty,NetQty)
            VALUES(@otherNet,@otherGross,1,1,0,@otherNet,0,N'采购',N'OTHER-SKU',2,2);
            INSERT dbo.PurchaseOrderHead(Uid,OriginKind,ConfirmationStatus,LifecycleStatus,OrderNo,CustomerId,CustomerCode,Currency,SupplierId,SupplierCode,ExecutionRequestId,OrderDate)
            VALUES(@purchase,1,0,0,N'PO-LINKED',@customerId,N'TEST-C',N'CNY',@supplierId,N'SUP',NEWID(),'2026-09-02'),
                  (@independent,2,0,0,N'PO-INDEPENDENT',@customerId,N'TEST-C',N'CNY',@supplierId,N'SUP',NEWID(),'2026-09-02');
            INSERT dbo.PurchaseOrderLine(Uid,PurchaseOrderId,LineNumber,OrderNo,Sku,UnitSnapshot,QTY)
            VALUES(@purchaseLine,@purchase,1,N'PO-LINKED',N'CONF-SKU',N'PCS',10),
                  (@independentLine,@independent,1,N'PO-INDEPENDENT',N'CONF-SKU',N'PCS',10);
            INSERT dbo.DemandExecutionLink(Uid,NetDemandId,ExecutionRequestId,CommandItemId,TargetType,TargetHeadId,TargetNo,TargetLineId,LinkStatus,AllocatedQty,ReleasedQty)
            VALUES(@ownLink,@net,NEWID(),NEWID(),N'PO',@purchase,N'PO-LINKED',@purchaseLine,0,6,0),
                  (@sharedLink,@otherNet,NEWID(),NEWID(),N'PO',@purchase,N'PO-LINKED',@purchaseLine,0,2,0);
            INSERT dbo.SupplierProgressReport(Uid,PurchaseOrderLineId,Sequence,ArrangementStatus,ReadyQty,ExpectedReadyDate,SupplierReportedDate,IsCorrection,CommandId)
            VALUES(NEWID(),@purchaseLine,1,2,8,'2026-10-01','2026-09-10',0,NEWID()),
                  (NEWID(),@purchaseLine,2,2,5,'2026-10-03','2026-09-11',1,NEWID());
            """, new { net, demand, fingerprint, otherGross, otherNet, purchase, purchaseLine,
                independent, independentLine, ownLink, sharedLink, customerId = db.CustomerId, supplierId = db.SupplierId });
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        var linked = fulfillment.Data!.Lines.Single().Purchases.Single();
        Assert.AreEqual(6m, linked.LinkedQty);
        Assert.AreEqual(5m, linked.PurchaseLineReadyQty);
        Assert.IsTrue(linked.IsSharedPurchaseLine);
        Assert.AreEqual(purchase, linked.PurchaseOrderId);
        await db.ExecuteAsync("""
            UPDATE dbo.Product SET Unit=N'KG',Sku=N'CONF-SKU' WHERE Uid=@componentId;
            UPDATE dbo.NetDemand SET ProductId=@componentId WHERE Uid=@net;
            UPDATE dbo.PurchaseOrderLine SET UnitSnapshot=N'KG' WHERE Uid=@purchaseLine;
            """, new { componentId = db.ComponentId, net, purchaseLine });
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.AreEqual(6m, fulfillment.Data!.Lines.Single().Purchases.Single().LinkedQty);
        Assert.AreEqual(string.Empty, fulfillment.Data.Lines.Single().Purchases.Single().ReviewReason);
        var childNet = Guid.NewGuid(); var childLink = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT dbo.NetDemand(Uid,GrossDemandId,SourceDemandVersionSnapshot,SourceVersionSnapshot,
                SourceDemandBasisFingerprintSnapshot,DecompositionRevision,LineNumber,LifecycleStatus,
                RootNetDemandId,ParentNetDemandId,ProductId,DeliveryMode,SupplyTypeSnapshot,SkuSnapshot,GrossQty,NetQty)
            VALUES(@childNet,@demand,(SELECT Version FROM dbo.GrossDemand WHERE Uid=@demand),1,@fingerprint,
                1,2,0,@net,@net,@componentId,0,N'采购',N'CONF-SKU',2,2);
            INSERT dbo.DemandExecutionLink(Uid,NetDemandId,ExecutionRequestId,CommandItemId,TargetType,
                TargetHeadId,TargetNo,TargetLineId,LinkStatus,AllocatedQty,ReleasedQty)
            VALUES(@childLink,@childNet,NEWID(),NEWID(),N'PO',@purchase,N'PO-LINKED',@purchaseLine,0,2,0);
            """, new { childNet, childLink, demand, fingerprint, net, componentId = db.ComponentId, purchase, purchaseLine });
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.HasCount(1, fulfillment.Data!.Lines.Single().Purchases);
        Assert.AreEqual(8m, fulfillment.Data.Lines.Single().Purchases.Single().LinkedQty);
        Assert.AreEqual(5m, fulfillment.Data.Lines.Single().Purchases.Single().PurchaseLineReadyQty);
        await db.ExecuteAsync("UPDATE dbo.NetDemand SET ProductId=@productId WHERE Uid=@childNet",
            new { productId = db.ProductId, childNet });
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.HasCount(1, fulfillment.Data!.Lines.Single().Purchases);
        Assert.IsNull(fulfillment.Data.Lines.Single().Purchases.Single().LinkedQty);
        StringAssert.Contains(fulfillment.Data.Lines.Single().Purchases.Single().ReviewReason, "单位");
        await db.ExecuteAsync("UPDATE dbo.DemandExecutionLink SET ReleasedQty=AllocatedQty,LinkStatus=8 WHERE Uid=@childLink", new { childLink });
        await db.ExecuteAsync("UPDATE dbo.NetDemand SET ProductId=NULL WHERE Uid=@net", new { net });
        await db.ExecuteAsync("UPDATE dbo.PurchaseOrderLine SET UnitSnapshot=NULL WHERE Uid=@purchaseLine", new { purchaseLine });
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.IsNull(fulfillment.Data!.Lines.Single().Purchases.Single().LinkedQty);
        StringAssert.Contains(fulfillment.Data.Lines.Single().Purchases.Single().ReviewReason, "单位");
        await db.ExecuteAsync("UPDATE dbo.PurchaseOrderLine SET UnitSnapshot=N'PCS' WHERE Uid=@purchaseLine", new { purchaseLine });
        await db.ExecuteAsync("UPDATE dbo.DemandExecutionLink SET ReleasedQty=AllocatedQty,LinkStatus=8 WHERE Uid=@ownLink", new { ownLink });
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.AreEqual(0, fulfillment.Data!.Lines.Single().Purchases.Count);
        await db.ExecuteAsync("""
            INSERT dbo.DemandExecutionLink(Uid,NetDemandId,ExecutionRequestId,CommandItemId,TargetType,TargetHeadId,TargetNo,TargetLineId,LinkStatus,AllocatedQty,ReleasedQty)
            VALUES(NEWID(),@net,NEWID(),NEWID(),N'PO',NEWID(),N'PO-MISSING',NEWID(),0,1,0)
            """, new { net });
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.AreEqual("需核实", fulfillment.Data!.Lines.Single().PurchaseState);
        StringAssert.Contains(fulfillment.Data.Lines.Single().PurchaseReviewReason, "证据异常");
        var secondPacking = Guid.NewGuid(); var secondLine = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT dbo.PackingListHead(Uid,PackingListNo,CustomerId,InvoiceDate,LifecycleStatus,PostingStatus,PostingDate,PostedDate,CreateCommandId)
            VALUES(@secondPacking,N'PL-TEST-2',@customerId,'2026-09-02',0,1,'2026-09-02','2026-09-02',NEWID());
            INSERT dbo.PackingListLine(Uid,PackingListHeadId,LineNumber,GrossDemandId,IsNonStandardSnapshot,SourceTypeSnapshot,SourceDocumentNoSnapshot,
                                         SourceLineNumberSnapshot,SkuSnapshot,UnitSnapshot,Qty)
            VALUES(@secondLine,@secondPacking,1,@demand,1,N'SO',N'SO-CONFIRMED',1,N'CONF-SKU',N'PCS',3);
            """, new { secondPacking, secondLine, demand, customerId = db.CustomerId });
        Assert.AreEqual(7m, (await Single(new() { QuickView = SalesOrderManagerQuickView.PartiallyShipped })).TotalShippedQty);
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.AreEqual(2, fulfillment.Data!.Lines.Single().Deliveries.Count);
        Assert.AreEqual(7m, fulfillment.Data.Lines.Single().ShippedQty);
        async Task<int> CountAt(SalesOrderManagerSearchRequest request, DateTime today)
        {
            var page = await db.InScopeAsync(p => p.GetRequiredService<ISalesOrderQueryRepository>()
                .SearchManagerAsync(request, today));
            return page.TotalCount;
        }
        Assert.AreEqual(1, await CountAt(new() { QuickView = SalesOrderManagerQuickView.DueSoon }, new DateTime(2026, 9, 25)));
        Assert.AreEqual(0, await CountAt(new() { QuickView = SalesOrderManagerQuickView.DueSoon }, new DateTime(2026, 9, 24)));
        Assert.AreEqual(1, await CountAt(new() { QuickView = SalesOrderManagerQuickView.Overdue }, new DateTime(2026, 10, 3)));
        Assert.AreEqual(1, await CountAt(new() { EffectiveDeliveryDateFrom = new DateTime(2026, 10, 2),
            EffectiveDeliveryDateTo = new DateTime(2026, 10, 2), CustomerSkus = ["CUSTOM-CONF"] }, new DateTime(2026, 9, 25)));
        var firstPage = await db.InScopeAsync(p => p.GetRequiredService<ISalesOrderQueryRepository>()
            .SearchManagerAsync(new() { PageIndex = 1, PageSize = 1 }, new DateTime(2026, 9, 25)));
        var secondPage = await db.InScopeAsync(p => p.GetRequiredService<ISalesOrderQueryRepository>()
            .SearchManagerAsync(new() { PageIndex = 2, PageSize = 1 }, new DateTime(2026, 9, 25)));
        Assert.AreEqual(2, firstPage.TotalCount);
        Assert.AreEqual(2, secondPage.TotalCount);
        Assert.AreNotEqual(firstPage.Items.Single().Uid, secondPage.Items.Single().Uid);
        await db.ExecuteAsync("UPDATE dbo.PackingListHead SET PostingStatus=8,ReversedDate='2026-09-03' WHERE Uid=@packing", new { packing });
        Assert.AreEqual(3m, (await Single(new() { QuickView = SalesOrderManagerQuickView.PartiallyShipped })).TotalShippedQty);
        await db.ExecuteAsync("UPDATE dbo.PackingListHead SET PostingStatus=8,ReversedDate='2026-09-03' WHERE Uid=@secondPacking", new { secondPacking });
        var reversed = await Single(new() { QuickView = SalesOrderManagerQuickView.NotShipped, InvoiceNo = "INV-CONFIRMED" });
        Assert.AreEqual(0m, reversed.TotalShippedQty);
        Assert.AreEqual(10m, reversed.TotalUnshippedQty);
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.AreEqual(0m, fulfillment.Data!.Lines.Single().ShippedQty);
        Assert.IsTrue(fulfillment.Data.Lines.Single().Deliveries.All(row => !row.CountsTowardDelivery));
        await db.ExecuteAsync("UPDATE dbo.PackingListHead SET PostingStatus=1,ReversedDate=NULL WHERE Uid=@packing; UPDATE dbo.PackingListLine SET Qty=12 WHERE Uid=@packingLine", new { packing, packingLine });
        var overage = await Single(new() { OrderNo = "SO-CONFIRMED" });
        Assert.AreEqual(12m, overage.TotalShippedQty);
        Assert.AreEqual(0m, overage.TotalUnshippedQty);
        Assert.IsTrue(overage.NeedsReview);
        Assert.IsNull(overage.FulfillmentStatus);
        Assert.AreEqual(0, await CountAt(new() { QuickView = SalesOrderManagerQuickView.FullyShipped }, new DateTime(2026, 9, 25)));
        Assert.AreEqual(confirmed, (await Single(new() { QuickView = SalesOrderManagerQuickView.NeedsReview })).Uid);
        await db.ExecuteAsync("UPDATE dbo.GrossDemand SET DemandBasisFingerprint=NULL WHERE Uid=@demand", new { demand });
        var legacy = await Single(new() { QuickView = SalesOrderManagerQuickView.NeedsReview });
        Assert.IsNull(legacy.TotalShippedQty);
        var legacyDetail = await db.InScopeAsync(p => p.GetRequiredService<ISalesOrderQueryService>().GetAsync(confirmed));
        Assert.IsTrue(legacyDetail.Success, legacyDetail.Message);
        Assert.IsNull(legacyDetail.Data!.Lines.Single().ShippedQty);
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.IsNull(fulfillment.Data!.Lines.Single().ShippedQty);
        Assert.IsNull(fulfillment.Data.Lines.Single().GrossDemandId);
        await db.ExecuteAsync("UPDATE dbo.GrossDemand SET DemandBasisFingerprint=@fingerprint WHERE Uid=@demand; UPDATE dbo.PackingListHead SET PostingStatus=8,ReversedDate='2026-09-03' WHERE Uid=@packing; UPDATE dbo.SalesOrderHeader SET DocumentStatus=9 WHERE Uid=@confirmed", new { demand, packing, confirmed, fingerprint });
        var closed = await Single(new() { QuickView = SalesOrderManagerQuickView.ClosedWithOutstanding });
        Assert.AreEqual(10m, closed.TotalUnshippedQty);
        fulfillment = await db.InScopeAsync(p => p.GetRequiredService<ISalesFulfillmentQueryService>().GetAsync(confirmed));
        Assert.AreEqual(10m, fulfillment.Data!.Lines.Single().UnshippedQty);
        Assert.AreEqual(0m, fulfillment.Data.Lines.Single().AllowedFurtherDeliveryQty);
        var empty = Guid.NewGuid();
        await db.ExecuteAsync("INSERT dbo.SalesOrderHeader(Uid,DocumentStatus,ConfirmationStatus,OrderNo,CustomerId,CustomerCode,LeadTimeDays) VALUES(@empty,0,0,N'SO-EMPTY',@customerId,N'TEST-C',7)", new { empty, customerId = db.CustomerId });
        var emptyItem = await Single(new() { QuickView = SalesOrderManagerQuickView.NeedsReview });
        Assert.AreEqual(empty, emptyItem.Uid);
        Assert.IsFalse(emptyItem.HasUndatedOpenLine);
        var mixed = Guid.NewGuid(); var mixedLineA = Guid.NewGuid(); var mixedLineB = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT dbo.SalesOrderHeader(Uid,DocumentStatus,ConfirmationStatus,OrderNo,CustomerId,CustomerCode,LeadTimeDays)
            VALUES(@mixed,0,0,N'SO-MIXED',@customerId,N'TEST-C',7);
            INSERT dbo.SalesOrderLine(Uid,LineNumber,SalesOrderId,IsNonStandard,Sku,Qty,UnitSnapshot)
            VALUES(@mixedLineA,1,@mixed,1,N'MIX-A',2,N'PCS'),(@mixedLineB,2,@mixed,1,N'MIX-B',3,N'BOX');
            """, new { mixed, mixedLineA, mixedLineB, customerId = db.CustomerId });
        var mixedItem = await Single(new() { OrderNo = "SO-MIXED" });
        Assert.IsNull(mixedItem.TotalQty);
        Assert.IsNull(mixedItem.TotalShippedQty);
        Assert.IsFalse(mixedItem.NeedsReview);
        await db.ExecuteAsync("UPDATE dbo.GrossDemand SET UnitSnapshot=N'BOX' WHERE Uid=@demand; UPDATE dbo.SalesOrderHeader SET DocumentStatus=0 WHERE Uid=@confirmed", new { demand, confirmed });
        var mismatch = await Single(new() { QuickView = SalesOrderManagerQuickView.NeedsReview, OrderNo = "SO-CONFIRMED" });
        Assert.IsNull(mismatch.TotalShippedQty);
        Assert.AreEqual(0, await CountAt(new() { QuickView = SalesOrderManagerQuickView.Overdue, OrderNo = "SO-CONFIRMED" }, new DateTime(2026, 10, 3)));
        await db.ExecuteAsync("UPDATE dbo.GrossDemand SET UnitSnapshot=N'PCS',SourceNo=N'SO-WRONG' WHERE Uid=@demand", new { demand });
        var wrongSource = await Single(new() { QuickView = SalesOrderManagerQuickView.NeedsReview, OrderNo = "SO-CONFIRMED" });
        StringAssert.Contains(wrongSource.ReviewReason, "来源身份");
        await db.ExecuteAsync("""
            UPDATE dbo.GrossDemand SET SourceNo=N'SO-CONFIRMED' WHERE Uid=@demand;
            UPDATE dbo.PackingListHead SET PostingStatus=1,ReversedDate=NULL WHERE Uid=@packing;
            UPDATE dbo.PackingListLine SET SourceDocumentNoSnapshot=N'SO-WRONG' WHERE Uid=@packingLine;
            """, new { demand, packing, packingLine });
        var badHistory = await Single(new() { QuickView = SalesOrderManagerQuickView.NeedsReview, OrderNo = "SO-CONFIRMED" });
        Assert.IsNull(badHistory.TotalShippedQty);
        StringAssert.Contains(badHistory.ReviewReason, "装箱过账历史");
        await db.ExecuteAsync("""
            INSERT dbo.GrossDemand(Uid,LifecycleStatus,SourceType,SourceUid,SourceNo,SourceLineId,SourceLineNumber,SourceVersion,
                                    DemandBasisFingerprint,CustomerId,IsNonStandard,SkuSnapshot,CustomerSkuSnapshot,UnitSnapshot,DemandQty,RequiredDate)
            VALUES(NEWID(),9,N'SO',@draft,N'SO-DRAFT',@draftLine,1,1,REPLICATE(N'A',64),@customerId,1,N'DRAFT-SKU',N'CUSTOM-DRAFT',N'PCS',10,'2026-10-01');
            """, new { draft, draftLine, customerId = db.CustomerId });
        var pendingWithHistory = await Single(new() { QuickView = SalesOrderManagerQuickView.NeedsReview, OrderNo = "SO-DRAFT" });
        Assert.IsNull(pendingWithHistory.TotalShippedQty);
        StringAssert.Contains(pendingWithHistory.ReviewReason, "待确认仍有关联来源");
    }
}
