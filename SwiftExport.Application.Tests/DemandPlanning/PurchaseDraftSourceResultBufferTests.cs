using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;

namespace SwiftExport.Application.Tests.DemandPlanning;

[TestClass]
public sealed class PurchaseDraftSourceResultBufferTests
{
    [TestMethod]
    public void Rollback_AfterEarlierDocumentWasStaged_DoesNotLeakPurchaseDrafts()
    {
        var result = new CreatePurchaseDraftSourceResult();
        var buffer = new PurchaseDraftSourceResultBuffer(result);
        buffer.Stage(new CreatedPurchaseDraftDto
        {
            PurchaseOrderId = Guid.NewGuid(),
            OrderNo = "PO-FIRST"
        });

        // 模拟同来源第二个供应商文档失败，数据库事务整体回滚。
        buffer.Rollback();

        Assert.IsEmpty(result.PurchaseDrafts);
    }

    [TestMethod]
    public void PublishCommitted_ReleasesAllStagedDocumentsTogether()
    {
        var result = new CreatePurchaseDraftSourceResult();
        var buffer = new PurchaseDraftSourceResultBuffer(result);
        buffer.Stage(new CreatedPurchaseDraftDto { PurchaseOrderId = Guid.NewGuid(), OrderNo = "PO-ONE" });
        buffer.Stage(new CreatedPurchaseDraftDto { PurchaseOrderId = Guid.NewGuid(), OrderNo = "PO-TWO" });

        buffer.PublishCommitted();

        Assert.HasCount(2, result.PurchaseDrafts);
    }
}
