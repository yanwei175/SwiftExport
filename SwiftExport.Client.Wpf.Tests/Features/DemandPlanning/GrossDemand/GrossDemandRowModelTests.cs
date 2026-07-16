using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.GrossDemand;

[TestClass]
public sealed class GrossDemandRowModelTests
{
    [TestMethod]
    public void FromDto_ShouldCopyReadOnlyFactsAndKeepSelectionClientSide()
    {
        var uid = Guid.NewGuid();
        var dto = new GrossDemandListItemDto
        {
            Uid = uid,
            Version = 4,
            LifecycleStatus = GrossDemandLifecycleStatusEnum.Active,
            DecompositionStatus = GrossDemandDecompositionStatusEnum.Pending,
            SourceType = "SO",
            SourceNo = "SO-1001",
            Sku = "A-100",
            DemandQty = 12m,
            ExecutionProgress = DemandExecutionProgressEnum.NotStarted,
            CanDecompose = true,
        };

        var row = GrossDemandRowModel.FromDto(dto);

        Assert.AreEqual(uid, row.Uid);
        Assert.AreEqual(4, row.Version);
        Assert.AreEqual("销售", row.SourceTypeText);
        Assert.AreEqual("待拆解", row.DecompositionStatusText);
        Assert.IsTrue(row.CanDecompose);
        Assert.IsFalse(row.IsChecked);

        row.IsChecked = true;
        Assert.IsTrue(row.IsChecked);
        Assert.IsFalse(dto.GetType().GetProperties().Any(property => property.Name == nameof(row.IsChecked)));
    }
}
