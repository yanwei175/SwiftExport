using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.GrossDemand;

[TestClass]
public sealed class GrossDemandRowModelTests
{
    /// <summary>
    /// 验证 DTO 映射保留只读事实、统一来源文案，并将选择状态限制在客户端模型内。
    /// </summary>
    [TestMethod]
    public void FromDto_ShouldCopyReadOnlyFactsAndKeepSelectionClientSide()
    {
        var uid = Guid.NewGuid();
        var dto = new GrossDemandListItemDto
        {
            Uid = uid,
            Version = 4,
            LifecycleStatus = GrossDemandLifecycleStatusEnum.Active,
            SourceType = "SO",
            SourceNo = "SO-1001",
            Sku = "A-100",
            DemandQty = 12m,
        };

        var row = GrossDemandRowModel.FromDto(dto);

        Assert.AreEqual(uid, row.Uid);
        Assert.AreEqual(4, row.Version);
        Assert.AreEqual("销售单", row.SourceTypeText);
        Assert.IsFalse(row.GetType().GetProperties().Any(p => p.Name == "IsChecked"));
    }
}
