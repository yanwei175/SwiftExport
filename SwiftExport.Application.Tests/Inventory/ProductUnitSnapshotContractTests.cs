using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Posting;
using SwiftExport.Application.Services.Inventory.Posting;

namespace SwiftExport.Application.Tests.Inventory;

[TestClass]
public sealed class ProductUnitSnapshotContractTests
{
    [TestMethod]
    public void PostingItem_MustCarrySkuAndUnitSnapshots()
    {
        Assert.IsNotNull(typeof(InventoryPostingItem).GetProperty(nameof(InventoryPostingItem.SkuSnapshot)));
        Assert.IsNotNull(typeof(InventoryPostingItem).GetProperty(nameof(InventoryPostingItem.UnitSnapshot)));
    }

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
            "InventoryPostingRepository.cs");

        StringAssert.Contains(postingSource, "SkuSnapshot = balance.SkuSnapshot");
        StringAssert.Contains(postingSource, "ProductNameSnapshot = balance.ProductNameSnapshot");
        StringAssert.Contains(postingSource, "UnitSnapshot = balance.UnitSnapshot");
        StringAssert.Contains(repositorySource, "SET SkuSnapshot=@SkuSnapshot,ProductNameSnapshot=@ProductNameSnapshot,UnitSnapshot=@UnitSnapshot");
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
