using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Wpf.EnterpriseGrid.Exporting;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Services;
using System.IO;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

[TestClass]
public sealed class NetDemandTraceExportBuilderTests
{
    [TestMethod]
    public async Task CreateRequest_ShouldFlattenAllocationAndLinkAndProduceXlsxWithoutVisualTree()
    {
        var allocationId = Guid.NewGuid();
        var summary = new NetDemandListItemDto
        {
            SourceType = "SO",
            SourceNo = "SO-1001",
            SourceLineNumber = 2,
            Sku = "A-100",
            NetQty = 12m,
            DemandRequiredDate = new DateTime(2026, 8, 10),
        };
        var allocation = new NetDemandAllocationDto
        {
            Uid = allocationId,
            PlanRevision = 3,
            LifecycleStatus = NetDemandAllocationLifecycleStatusEnum.Active,
            ExecutionType = DemandExecutionTypeEnum.Purchase,
            PurchaseSupplierName = "供应商甲",
            PlannedQty = 12m,
            PlannedDeliveryDate = new DateTime(2026, 8, 8),
        };
        var link = new NetDemandExecutionLinkDto
        {
            Uid = Guid.NewGuid(),
            NetDemandAllocationId = allocationId,
            TargetType = "PO",
            TargetNo = "PO-2001",
            TargetLineNumber = 1,
            AllocatedQty = 8m,
        };

        var request = NetDemandTraceExportBuilder.CreateRequest(summary, [allocation], [link]);

        Assert.AreEqual(18, request.Columns.Count);
        Assert.AreEqual(1, request.Rows.Count);
        Assert.AreEqual("SO-1001", request.Rows[0].Cells[1].DisplayText);
        Assert.AreEqual("PO-2001", request.Rows[0].Cells[13].DisplayText);
        Assert.AreEqual(8m, request.Rows[0].Cells[15].RawValue);

        await using var stream = new MemoryStream();
        await new XlsxGridExportProvider().ExportAsync(request, stream);
        Assert.IsGreaterThan(0, stream.Length);
        Assert.AreEqual((byte)'P', stream.ToArray()[0]);
        Assert.AreEqual((byte)'K', stream.ToArray()[1]);
    }

    [TestMethod]
    public void CreateRequest_ShouldExportSourceLevelPurchaseTraceWithReleasedQuantity()
    {
        var trace = new SourcePurchaseTraceDto
        {
            SourceType = "SO",
            SourceUid = Guid.NewGuid(),
            SourceNo = "SO-2002",
            Lines =
            {
                new SourcePurchaseTraceLineDto
                {
                    SourceLineNumber = 3,
                    Sku = "B-200",
                    NetDemandQty = 10,
                    OriginalLinkQty = 8,
                    ReleasedQty = 2,
                    LinkQty = 6,
                    PurchaseOrderNo = "PO-3003",
                    SupplierCode = "S01",
                    SupplierName = "供应商甲",
                },
            },
        };

        var request = NetDemandTraceExportBuilder.CreateRequest(trace);

        Assert.AreEqual(16, request.Columns.Count);
        Assert.AreEqual("SO-2002", request.Rows.Single().Cells[1].DisplayText);
        Assert.AreEqual(8m, request.Rows.Single().Cells[6].RawValue);
        Assert.AreEqual(2m, request.Rows.Single().Cells[7].RawValue);
        Assert.AreEqual("PO-3003", request.Rows.Single().Cells[9].DisplayText);
    }
}
