using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

[TestClass]
public sealed class NetDemandRowModelTests
{
    [TestMethod]
    public void FromDto_ShouldKeepAllocationIdentityAndClientSelectionSeparate()
    {
        var allocationId = Guid.NewGuid();
        var dto = new NetDemandListItemDto
        {
            Uid = Guid.NewGuid(),
            AllocationId = allocationId,
            AllocationVersion = 4,
            SourceType = "SO",
            SourceNo = "SO-1001",
            Sku = "B-100",
            RootSku = "A-100",
            IsCustomerSupplied = true,
            ExecutionType = DemandExecutionTypeEnum.Purchase,
            PlanStatus = NetDemandPlanStatusEnum.PartiallyAllocated,
            NetQty = 20m,
            PlannedQty = 20m,
            AllocatedQty = 8m,
            RemainingQty = 12m,
        };

        var row = NetDemandRowModel.FromDto(dto);

        Assert.AreEqual(allocationId, row.AllocationId);
        Assert.AreEqual(4, row.AllocationVersion);
        Assert.AreEqual("客供件", row.DemandRoleText);
        Assert.AreEqual("部分转采购", row.PlanStatusText);
        Assert.AreEqual(12m, row.RemainingQty);
        StringAssert.StartsWith(row.DisplaySku, "    ↳");
        Assert.IsFalse(row.IsChecked);
        Assert.IsFalse(dto.GetType().GetProperties().Any(property => property.Name == nameof(row.IsChecked)));
    }
}
