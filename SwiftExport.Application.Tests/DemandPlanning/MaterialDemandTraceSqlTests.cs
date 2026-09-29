using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>以隔离 LocalDB 验证物料列表和共享采购行的真实查询口径。</summary>
[TestClass, DoNotParallelize]
public sealed class MaterialDemandTraceSqlTests
{
    /// <summary>验证筛选先于分页、共享行收货覆盖、反向和历史追踪。</summary>
    [TestMethod]
    public async Task SearchAndTrace_RespectLinkedQtyAndCurrentReceiptCoverage()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_SQL_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_SQL_TESTS=1 后运行隔离 LocalDB 测试。");
        await using var db = new IndependentProcurementSqlFixture();
        await db.InitializeAsync();
        var demand = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var order = Guid.NewGuid(); var line = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT dbo.GrossDemand(Uid,LifecycleStatus,SourceType,SourceUid,SourceNo,SourceLineId,
                                    CustomerId,DemandBasisFingerprint,IsNonStandard,SkuSnapshot,UnitSnapshot,DemandQty)
            VALUES(@demand,0,N'MD',@demand,N'MD-MATERIAL',@demand,@customerId,N'MATERIAL-BASIS',1,N'SKU',N'PCS',12);
            INSERT dbo.NetDemand(Uid,GrossDemandId,SourceDemandVersionSnapshot,SourceDemandBasisFingerprintSnapshot,
                                 DecompositionRevision,LineNumber,LifecycleStatus,RootNetDemandId,DeliveryMode,
                                 SupplyTypeSnapshot,SkuSnapshot,GrossQty,NetQty,RequiredDate)
            VALUES(@first,@demand,(SELECT Version FROM dbo.GrossDemand WHERE Uid=@demand),N'MATERIAL-BASIS',
                   1,1,0,@first,0,N'采购',N'SKU',6,6,'2026-09-30'),
                  (@second,@demand,(SELECT Version FROM dbo.GrossDemand WHERE Uid=@demand),N'MATERIAL-BASIS',
                   1,2,0,@second,0,N'采购',N'SKU',6,6,'2026-10-01');
            INSERT dbo.PurchaseOrderHead(Uid,OriginKind,ConfirmationStatus,LifecycleStatus,OrderNo,CustomerId,
                                          CustomerCode,Currency,SupplierId,SupplierCode,ExecutionRequestId,OrderDate)
            VALUES(@order,1,0,0,N'PO-MATERIAL',@customerId,N'TEST-C',N'CNY',@supplierId,N'SUP',NEWID(),'2026-09-02');
            INSERT dbo.PurchaseOrderLine(Uid,PurchaseOrderId,LineNumber,OrderNo,Sku,UnitSnapshot,QTY)
            VALUES(@line,@order,1,N'PO-MATERIAL',N'SKU',N'PCS',12);
            INSERT dbo.DemandExecutionLink(Uid,NetDemandId,ExecutionRequestId,CommandItemId,TargetType,
                                             TargetHeadId,TargetNo,TargetLineId,LinkStatus,AllocatedQty,ReleasedQty)
            VALUES(NEWID(),@first,NEWID(),NEWID(),N'PO',@order,N'PO-MATERIAL',@line,0,6,0),
                  (NEWID(),@second,NEWID(),NEWID(),N'PO',@order,N'PO-MATERIAL',@line,0,6,0);
            DECLARE @receipt uniqueidentifier=NEWID();
            INSERT dbo.InboundDocument(Uid,DocumentNo,DocumentType,CreationMode,LifecycleStatus,PostingStatus,WarehouseSiteId,ReceiptDate)
            VALUES(@receipt,N'IR-MATERIAL',10,0,0,1,@warehouseId,'2026-09-03');
            INSERT dbo.InboundDocumentLine(Uid,InboundDocumentId,LineNumber,ProductId,SkuSnapshot,UnitSnapshot,Quantity,SourceLineId)
            VALUES(NEWID(),@receipt,1,@productId,N'SKU',N'PCS',7,@line);
            """, new { demand, first, second, order, line, customerId = db.CustomerId,
                supplierId = db.SupplierId, warehouseId = db.ReceiptSiteId, productId = db.ProductId });

        var list = await db.InScopeAsync(p => p.GetRequiredService<IMaterialDemandQueryService>()
            .SearchManagerAsync(new MaterialDemandManagerSearchRequest
            { QuickView = "Assigned", PageIndex = 1, PageSize = 1, PurchaseOrderNo = "PO-MATERIAL" }));
        Assert.IsTrue(list.Success, list.Message);
        Assert.AreEqual(2, list.Data!.TotalCount);
        Assert.HasCount(1, list.Data.Items);
        Assert.AreEqual(6m, list.Data.Items.Single().AssociatedPurchaseQty);
        var firstTrace = await db.InScopeAsync(p => p.GetRequiredService<IMaterialDemandQueryService>().GetTraceAsync(first));
        var secondTrace = await db.InScopeAsync(p => p.GetRequiredService<IMaterialDemandQueryService>().GetTraceAsync(second));
        Assert.IsTrue(firstTrace.Success, firstTrace.Message);
        Assert.AreEqual(6m, firstTrace.Data!.CurrentReceiptCoverageQty);
        Assert.AreEqual(1m, secondTrace.Data!.CurrentReceiptCoverageQty);
        Assert.AreEqual(7m, secondTrace.Data.Purchases.Single().PurchaseLineReceivedQty);

        await db.ExecuteAsync("UPDATE dbo.InboundDocument SET PostingStatus=8 WHERE DocumentNo=N'IR-MATERIAL'");
        firstTrace = await db.InScopeAsync(p => p.GetRequiredService<IMaterialDemandQueryService>().GetTraceAsync(first));
        Assert.IsFalse(firstTrace.Success);
        StringAssert.Contains(firstTrace.Message, "收货量未知");
        await db.ExecuteAsync("UPDATE dbo.InboundDocument SET PostingStatus=1 WHERE DocumentNo=N'IR-MATERIAL'");
        await db.ExecuteAsync("UPDATE dbo.DemandExecutionLink SET ReleasedQty=AllocatedQty,LinkStatus=8 WHERE NetDemandId=@first", new { first });
        await db.ExecuteAsync("UPDATE dbo.NetDemand SET LifecycleStatus=8 WHERE Uid=@first", new { first });
        var history = await db.InScopeAsync(p => p.GetRequiredService<IMaterialDemandQueryService>()
            .SearchManagerAsync(new MaterialDemandManagerSearchRequest
            { LifecycleStatus = NetDemandLifecycleStatusEnum.RolledBack }));
        Assert.AreEqual(1, history.Data!.TotalCount);
        firstTrace = await db.InScopeAsync(p => p.GetRequiredService<IMaterialDemandQueryService>().GetTraceAsync(first));
        Assert.IsNull(firstTrace.Data!.CurrentReceiptCoverageQty);
        Assert.HasCount(1, firstTrace.Data.Purchases);

        await db.ExecuteAsync("UPDATE dbo.GrossDemand SET DemandBasisFingerprint=NULL WHERE Uid=@demand", new { demand });
        var review = await db.InScopeAsync(p => p.GetRequiredService<IMaterialDemandQueryService>()
            .SearchManagerAsync(new MaterialDemandManagerSearchRequest { QuickView = "Review" }));
        Assert.AreEqual(1, review.Data!.TotalCount);
        Assert.IsNull(review.Data.Items.Single().AssociatedPurchaseQty);
        Assert.AreEqual(string.Empty, review.Data.Items.Single().DueHint);
        await db.ExecuteAsync("UPDATE dbo.GrossDemand SET DemandBasisFingerprint=N'MATERIAL-BASIS' WHERE Uid=@demand", new { demand });
        await db.ExecuteAsync("UPDATE dbo.PurchaseOrderLine SET UnitSnapshot=N'BOX' WHERE Uid=@line", new { line });
        review = await db.InScopeAsync(p => p.GetRequiredService<IMaterialDemandQueryService>()
            .SearchManagerAsync(new MaterialDemandManagerSearchRequest { QuickView = "Review" }));
        Assert.AreEqual(1, review.Data!.TotalCount);
        StringAssert.Contains(review.Data.Items.Single().ReviewReason, "单位");
    }
}
