using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Posting;
using SwiftExport.Application.Services.Inventory.Posting;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>验证库存请求及 PG 持久化保留产品和单位快照；静态契约不代表真实数据库写入验证。</summary>
[TestClass]
public sealed class ProductUnitSnapshotContractTests
{
    /// <summary>过账明细必须携带原 SKU 与单位快照字段，不能改为依赖当前产品值。</summary>
    [TestMethod]
    public void PostingItem_MustCarrySkuAndUnitSnapshots()
    {
        Assert.IsNotNull(typeof(InventoryPostingItem).GetProperty(nameof(InventoryPostingItem.SkuSnapshot)));
        Assert.IsNotNull(typeof(InventoryPostingItem).GetProperty(nameof(InventoryPostingItem.UnitSnapshot)));
    }

    /// <summary>流水事实沿用余额快照，PG 余额更新同时持久化三项产品快照。</summary>
    [TestMethod]
    public void InventoryKernel_MustPersistSnapshotsToBalanceAndLedger()
    {
        var postingSource = Read(
            "SwiftExport.Application",
            "Services",
            "Inventory",
            "Posting",
            "Models",
            "PostingFact.cs");
        var repositorySource = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "Inventory",
            "Posting",
            "PostgreSqlInventoryPostingRepository.cs");

        StringAssert.Contains(postingSource, "SkuSnapshot = balance.SkuSnapshot");
        StringAssert.Contains(postingSource, "ProductNameSnapshot = balance.ProductNameSnapshot");
        StringAssert.Contains(postingSource, "UnitSnapshot = balance.UnitSnapshot");
        // 忽略 SQL 换行，仍要求同一 SET 子句原顺序写入三项快照，不以单独字段出现代替原断言。
        StringAssert.Contains(System.Text.RegularExpressions.Regex.Replace(repositorySource, @"\s+", string.Empty),
            "SETsku_snapshot=@SkuSnapshot,product_name_snapshot=@ProductNameSnapshot,unit_snapshot=@UnitSnapshot");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。");
        return string.Empty;
    }
}
