using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>在隔离 LocalDB 验证来源链路筛选、分页及装箱反向的真实 SQL。</summary>
[TestClass, DoNotParallelize]
public sealed class GrossDemandTraceSqlTests
{
    /// <summary>验证可信拆解和活动 Link 的贡献，取消后仍保留历史；旧来源版本不推断当前采购量。</summary>
    [TestMethod]
    public async Task MaterialAndPurchaseTrace_RespectSourceVersionAndLinkRelease()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_SQL_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_SQL_TESTS=1 后运行隔离 LocalDB 来源追踪测试。");
        await using var db = new IndependentProcurementSqlFixture();
        await db.InitializeAsync();
        var demand = Guid.NewGuid(); var root = Guid.NewGuid(); var material = Guid.NewGuid();
        var order = Guid.NewGuid(); var line = Guid.NewGuid(); var link = Guid.NewGuid();
        const string fingerprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        await db.ExecuteAsync("UPDATE dbo.Product SET Unit=N'KG' WHERE Uid=@componentId", new { componentId = db.ComponentId });
        await db.ExecuteAsync("""
            INSERT dbo.GrossDemand(Uid,LifecycleStatus,SourceType,SourceUid,SourceNo,SourceLineId,
                                    CustomerId,DemandBasisFingerprint,IsNonStandard,SkuSnapshot,UnitSnapshot,DemandQty)
            VALUES(@demand,0,N'MD',@demand,N'MD-TRACE',@demand,@customerId,@fingerprint,1,N'SKU',N'PCS',10);
            INSERT dbo.NetDemand(Uid,GrossDemandId,SourceDemandVersionSnapshot,SourceDemandBasisFingerprintSnapshot,
                                 DecompositionRevision,LineNumber,LifecycleStatus,RootNetDemandId,ParentNetDemandId,
                                 ProductId,DeliveryMode,SupplyTypeSnapshot,SkuSnapshot,GrossQty,NetQty)
            VALUES(@root,@demand,(SELECT Version FROM dbo.GrossDemand WHERE Uid=@demand),@fingerprint,
                   1,1,0,@root,NULL,@productId,0,N'采购',N'SKU',10,10),
                  (@material,@demand,(SELECT Version FROM dbo.GrossDemand WHERE Uid=@demand),@fingerprint,
                   1,2,0,@root,@root,@componentId,0,N'采购',N'TEST-B',10,10);
            INSERT dbo.PurchaseOrderHead(Uid,OriginKind,ConfirmationStatus,LifecycleStatus,OrderNo,CustomerId,
                                          CustomerCode,Currency,SupplierId,SupplierCode,ExecutionRequestId,OrderDate)
            VALUES(@order,1,0,0,N'PO-TRACE',@customerId,N'TEST-C',N'CNY',@supplierId,N'SUP',NEWID(),'2026-09-02');
            INSERT dbo.PurchaseOrderLine(Uid,PurchaseOrderId,LineNumber,OrderNo,Sku,UnitSnapshot,QTY)
            VALUES(@line,@order,1,N'PO-TRACE',N'TEST-B',N'KG',10);
            INSERT dbo.DemandExecutionLink(Uid,NetDemandId,ExecutionRequestId,CommandItemId,TargetType,
                                             TargetHeadId,TargetNo,TargetLineId,LinkStatus,AllocatedQty,ReleasedQty)
            VALUES(@link,@material,NEWID(),NEWID(),N'PO',@order,N'PO-TRACE',@line,0,6,0);
            """, new { demand, root, material, order, line, link, customerId = db.CustomerId,
                supplierId = db.SupplierId, productId = db.ProductId, componentId = db.ComponentId, fingerprint });
        await db.ExecuteAsync("""
            DECLARE @posted uniqueidentifier=NEWID();
            INSERT dbo.InboundDocument(Uid,DocumentNo,DocumentType,CreationMode,LifecycleStatus,PostingStatus,WarehouseSiteId,ReceiptDate)
            VALUES(@posted,N'IR-TRACE',10,0,0,1,@warehouseId,'2026-09-03');
            INSERT dbo.InboundDocumentLine(Uid,InboundDocumentId,LineNumber,ProductId,SkuSnapshot,UnitSnapshot,Quantity,SourceLineId)
            VALUES(NEWID(),@posted,1,@productId,N'TEST-B',N'KG',3,@line);
            """, new { line, warehouseId = db.ReceiptSiteId, productId = db.ComponentId });
        var trace = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(demand));
        Assert.IsTrue(trace.Success, trace.Message);
        Assert.AreEqual(6m, trace.Data!.Materials.Single(row => row.NetDemandId == material).ActivePurchaseQty);
        Assert.AreEqual(6m, trace.Data.Purchases.Single().LinkedQty);
        Assert.AreEqual(3m, trace.Data.Purchases.Single().PurchaseLineReceivedQty);
        var purchaseTrace = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderTraceService>().GetAsync(order));
        Assert.IsTrue(purchaseTrace.Success, purchaseTrace.Message);
        Assert.IsFalse(purchaseTrace.Data!.Lines.Single().Links.Single().NeedsReview);
        await db.ExecuteAsync("UPDATE dbo.PurchaseOrderLine SET UnitSnapshot=N'PCS' WHERE Uid=@line", new { line });
        trace = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(demand));
        Assert.IsNull(trace.Data!.Materials.Single(row => row.NetDemandId == material).ActivePurchaseQty);
        StringAssert.Contains(trace.Data.Purchases.Single().ReviewReason, "单位");
        purchaseTrace = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderTraceService>().GetAsync(order));
        Assert.IsTrue(purchaseTrace.Success, purchaseTrace.Message);
        Assert.IsTrue(purchaseTrace.Data!.Lines.Single().Links.Single().NeedsReview);
        await db.ExecuteAsync("UPDATE dbo.PurchaseOrderLine SET UnitSnapshot=N'KG' WHERE Uid=@line", new { line });
        var hasPurchase = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>()
            .SearchAsync(new GrossDemandSearchRequest { TraceView = "HasPurchase" }));
        Assert.AreEqual(1, hasPurchase.Data!.TotalCount);
        Assert.IsTrue(hasPurchase.Data.Items.Single().HasValidPurchase);
        await db.ExecuteAsync("UPDATE dbo.GrossDemand SET DemandBasisFingerprint=NULL WHERE Uid=@demand", new { demand });
        var unknownPurchase = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>()
            .SearchAsync(new GrossDemandSearchRequest { TraceView = "HasPurchase" }));
        Assert.AreEqual(0, unknownPurchase.Data!.TotalCount);
        await db.ExecuteAsync("UPDATE dbo.GrossDemand SET DemandBasisFingerprint=@fingerprint WHERE Uid=@demand",
            new { demand, fingerprint });

        await db.ExecuteAsync("UPDATE dbo.DemandExecutionLink SET ReleasedQty=AllocatedQty,LinkStatus=8 WHERE Uid=@link", new { link });
        trace = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(demand));
        Assert.AreEqual(0m, trace.Data!.Materials.Single(row => row.NetDemandId == material).ActivePurchaseQty);
        Assert.AreEqual(0m, trace.Data.Purchases.Single().LinkedQty);
        StringAssert.Contains(trace.Data.Purchases.Single().ReviewReason, "历史");
        var noPurchase = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>()
            .SearchAsync(new GrossDemandSearchRequest { TraceView = "NoPurchase" }));
        Assert.AreEqual(1, noPurchase.Data!.TotalCount);

        var orphan = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT dbo.DemandExecutionLink(Uid,NetDemandId,ExecutionRequestId,CommandItemId,TargetType,
                                             TargetHeadId,TargetNo,TargetLineId,LinkStatus,AllocatedQty,ReleasedQty)
            VALUES(@orphan,@material,NEWID(),NEWID(),N'PO',NEWID(),N'PO-MISSING',NEWID(),0,2,0)
            """, new { orphan, material });
        trace = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(demand));
        Assert.IsNull(trace.Data!.Materials.Single(row => row.NetDemandId == material).ActivePurchaseQty);
        StringAssert.Contains(trace.Data.Materials.Single(row => row.NetDemandId == material).ReviewReason, "目标");
        Assert.IsFalse(trace.Data.Purchases.Single(row => row.LinkedQty == 2m).HasPurchaseTarget);
        await db.ExecuteAsync("UPDATE dbo.DemandExecutionLink SET ReleasedQty=AllocatedQty,LinkStatus=8 WHERE Uid=@orphan", new { orphan });

        await db.ExecuteAsync("UPDATE dbo.GrossDemand SET DemandBasisFingerprint=NULL WHERE Uid=@demand", new { demand });
        trace = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(demand));
        Assert.IsNull(trace.Data!.Materials.Single(row => row.NetDemandId == material).ActivePurchaseQty);
        StringAssert.Contains(trace.Data.Materials.Single(row => row.NetDemandId == material).ReviewReason, "需核实");

        // 反向标志没有原/反向库存流水证明时，整行实收保持未知，不允许作为零收货展示。
        await db.ExecuteAsync("""
            INSERT dbo.InboundDocument(Uid,DocumentNo,DocumentType,CreationMode,LifecycleStatus,PostingStatus,WarehouseSiteId,ReceiptDate)
            VALUES(NEWID(),N'IR-UNVERIFIED-REVERSE',10,0,0,8,@warehouseId,'2026-09-03');
            """, new { warehouseId = db.ReceiptSiteId });
        var uncertain = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(demand));
        Assert.IsFalse(uncertain.Success);
        StringAssert.Contains(uncertain.Message, "收货量未知");
    }

    /// <summary>筛选先于分页；已过账数量可累计，反向行只保留为历史。</summary>
    [TestMethod]
    public async Task TraceAndFilters_UseCurrentLinksAndPostedDelivery()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_SQL_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_SQL_TESTS=1 后运行隔离 LocalDB 来源追踪测试。");
        await using var db = new IndependentProcurementSqlFixture();
        await db.InitializeAsync();
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var posted = Guid.NewGuid(); var reversed = Guid.NewGuid();
        const string fingerprint = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        await db.ExecuteAsync("""
            INSERT dbo.GrossDemand(Uid,LifecycleStatus,SourceType,SourceUid,SourceNo,SourceLineId,CustomerId,
                                    DemandBasisFingerprint,IsNonStandard,SkuSnapshot,UnitSnapshot,DemandQty)
            VALUES(@first,0,N'MD',@first,N'MD-1',@first,@customerId,@fingerprint,1,N'SKU-1',N'PCS',10),
                  (@second,0,N'MD',@second,N'MD-2',@second,@customerId,@fingerprint,1,N'SKU-2',N'PCS',5);
            INSERT dbo.PackingListHead(Uid,PackingListNo,CustomerId,InvoiceDate,LifecycleStatus,PostingStatus,PostedDate,ReversedDate,CreateCommandId)
            VALUES(@posted,N'PL-POSTED',@customerId,'2026-09-01',0,1,'2026-09-02',NULL,NEWID()),
                  (@reversed,N'PL-REVERSED',@customerId,'2026-09-01',0,8,'2026-09-02','2026-09-03',NEWID());
            INSERT dbo.PackingListLine(Uid,PackingListHeadId,LineNumber,GrossDemandId,IsNonStandardSnapshot,
                                        SourceTypeSnapshot,SourceDocumentNoSnapshot,SkuSnapshot,UnitSnapshot,Qty)
            VALUES(NEWID(),@posted,1,@first,1,N'MD',N'MD-1',N'SKU-1',N'PCS',4),
                  (NEWID(),@reversed,1,@first,1,N'MD',N'MD-1',N'SKU-1',N'PCS',3);
            """, new { first, second, posted, reversed, customerId = db.CustomerId, fingerprint });
        var trace = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(first));
        Assert.IsTrue(trace.Success, trace.Message);
        Assert.AreEqual(4m, trace.Data!.DeliveredQty);
        Assert.HasCount(2, trace.Data.Deliveries);
        Assert.AreEqual(1, trace.Data.Deliveries.Count(x => x.CountsTowardDelivery));
        await db.ExecuteAsync("UPDATE dbo.PackingListLine SET SkuSnapshot=N'WRONG' WHERE PackingListHeadId=@posted", new { posted });
        var wrongSnapshot = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(first));
        Assert.IsTrue(wrongSnapshot.Success, wrongSnapshot.Message);
        Assert.IsNull(wrongSnapshot.Data!.DeliveredQty);
        StringAssert.Contains(wrongSnapshot.Data.ReviewReason, "装箱历史");
        await db.ExecuteAsync("UPDATE dbo.PackingListLine SET SkuSnapshot=N'SKU-1' WHERE PackingListHeadId=@posted", new { posted });
        await db.ExecuteAsync("UPDATE dbo.GrossDemand SET DemandBasisFingerprint=NULL WHERE Uid=@first", new { first });
        var unverified = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(first));
        Assert.IsTrue(unverified.Success, unverified.Message);
        Assert.IsNull(unverified.Data!.DeliveredQty);
        StringAssert.Contains(unverified.Data.ReviewReason, "指纹");
        await db.ExecuteAsync("UPDATE dbo.GrossDemand SET DemandBasisFingerprint=@fingerprint WHERE Uid=@first", new { first, fingerprint });

        var hasDelivery = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>()
            .SearchAsync(new GrossDemandSearchRequest { TraceView = "HasDelivery", PageIndex = 1, PageSize = 1 }));
        Assert.IsTrue(hasDelivery.Success, hasDelivery.Message);
        Assert.AreEqual(1, hasDelivery.Data!.TotalCount);
        Assert.AreEqual(first, hasDelivery.Data.Items.Single().Uid);
        Assert.IsTrue(hasDelivery.Data.Items.Single().HasPostedDelivery);
        Assert.IsFalse(hasDelivery.Data.Items.Single().HasActiveMaterial);
        var noDelivery = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>()
            .SearchAsync(new GrossDemandSearchRequest { TraceView = "NoDelivery", PageIndex = 1, PageSize = 1 }));
        Assert.AreEqual(1, noDelivery.Data!.TotalCount);
        Assert.AreEqual(second, noDelivery.Data.Items.Single().Uid);
        var customer = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>()
            .SearchAsync(new GrossDemandSearchRequest { CustomerKeyword = "TEST-C", PageIndex = 1, PageSize = 1 }));
        Assert.AreEqual(2, customer.Data!.TotalCount);
        Assert.HasCount(1, customer.Data.Items);

        await db.ExecuteAsync("UPDATE dbo.GrossDemand SET SourceType=N'SO',SourceVersion=NULL WHERE Uid=@first", new { first });
        var legacySource = await db.InScopeAsync(p => p.GetRequiredService<IGrossDemandQueryService>().GetTraceAsync(first));
        Assert.IsTrue(legacySource.Success, legacySource.Message);
        Assert.IsNull(legacySource.Data!.DeliveredQty);
        StringAssert.Contains(legacySource.Data.ReviewReason, "版本");
    }
}
