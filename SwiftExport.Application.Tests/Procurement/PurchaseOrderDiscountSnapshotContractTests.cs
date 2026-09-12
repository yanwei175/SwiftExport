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

    /// <summary>增量脚本必须声明手工优惠边界，不得默认、回填或按倍率计算未知历史。</summary>
    [TestMethod]
    public void IncrementalSql_ShouldKeepUnknownHistoryNullAndAvoidDerivedTotals()
    {
        var sql = Read("SwiftExport.Infrastructure", "Database", "20260912_采购单增加折扣快照字段.sql");

        StringAssert.Contains(sql, "DiscountRate decimal(9,6) NULL");
        StringAssert.Contains(sql, "DiscountAmount decimal(18,4) NULL");
        StringAssert.Contains(sql, "CK_PurchaseOrderHead_DiscountRate");
        StringAssert.Contains(sql, "DiscountRate >= 0");
        StringAssert.Contains(sql, "用户手工填写的整单优惠金额快照");
        StringAssert.Contains(sql, "DiscountAmount 不由 DiscountRate 自动计算或覆盖");
        Assert.IsFalse(sql.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("UPDATE dbo.PurchaseOrderHead", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("DiscountedAmount", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("TotalAmount", StringComparison.OrdinalIgnoreCase));
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
