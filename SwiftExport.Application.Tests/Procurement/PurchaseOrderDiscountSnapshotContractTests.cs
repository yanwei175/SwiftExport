using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Core.Entities.Procurement;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>验证采购价格倍率与手工整单优惠快照保持可空、显式持久化且彼此不作自动换算。</summary>
[TestClass]
public sealed class PurchaseOrderDiscountSnapshotContractTests
{
    /// <summary>实体和详情契约必须以可空小数表达未填写或历史未知状态。</summary>
    [TestMethod]
    public void EntityAndDetailDto_ShouldExposeNullableDiscountSnapshots()
    {
        Assert.AreEqual(typeof(decimal?), typeof(PurchaseOrderHead).GetProperty(nameof(PurchaseOrderHead.DiscountRate))!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(PurchaseOrderHead).GetProperty(nameof(PurchaseOrderHead.DiscountAmount))!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(PurchaseOrderDetailDto).GetProperty(nameof(PurchaseOrderDetailDto.DiscountRate))!.PropertyType);
        Assert.AreEqual(typeof(decimal?), typeof(PurchaseOrderDetailDto).GetProperty(nameof(PurchaseOrderDetailDto.DiscountAmount))!.PropertyType);
    }

    /// <summary>新旧创建仓储和详情查询必须显式覆盖新增字段，避免依赖 SELECT 星号或丢失快照。</summary>
    [TestMethod]
    public void RepositorySql_ShouldPersistAndProjectDiscountSnapshots()
    {
        var commandRepository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "ProcurementOrderRepository.cs");
        var draftRepository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrderDraftRepository.cs");
        var queryRepository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "ProcurementOrderQueryRepository.cs");

        StringAssert.Contains(commandRepository, "Currency,DiscountRate,DiscountAmount");
        StringAssert.Contains(commandRepository, "@Currency,@DiscountRate,@DiscountAmount");
        StringAssert.Contains(draftRepository, "Currency,DiscountRate,DiscountAmount");
        StringAssert.Contains(draftRepository, "@Currency,@DiscountRate,@DiscountAmount");
        StringAssert.Contains(queryRepository, "h.Currency,h.DiscountRate,h.DiscountAmount");
    }

    /// <summary>实体生成基线必须声明手工优惠边界且不包含历史回填。</summary>
    [TestMethod]
    public void GeneratedSchema_ShouldKeepUnknownHistoryNullAndAvoidDerivedTotals()
    {
        var sql = Read("SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.Schema.sql");
        var tableStart = sql.IndexOf("CREATE TABLE [dbo].[PurchaseOrderHead]", StringComparison.Ordinal);
        var tableEnd = sql.IndexOf("CREATE TABLE [dbo].[PurchaseOrderLine]", tableStart, StringComparison.Ordinal);
        var purchaseHead = sql[tableStart..tableEnd];

        StringAssert.Contains(purchaseHead, "[DiscountRate] decimal(9,6) NULL");
        StringAssert.Contains(purchaseHead, "[DiscountAmount] decimal(18,4) NULL");
        StringAssert.Contains(purchaseHead, "CK_PurchaseOrderHead_Discount");
        StringAssert.Contains(purchaseHead, "[DiscountRate] >= 0");
        Assert.IsFalse(purchaseHead.Contains("UPDATE dbo.PurchaseOrderHead", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(purchaseHead.Contains("DiscountedAmount", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(purchaseHead.Contains("TotalAmount", StringComparison.OrdinalIgnoreCase));
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SwiftExport-WebAPI.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("无法定位 SwiftExport 仓库根目录。");
    }
}
