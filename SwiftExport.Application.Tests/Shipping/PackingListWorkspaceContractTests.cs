using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>保留实体和出货事实断言，并确保旧装箱单入口已移除。</summary>
[TestClass]
public sealed class PackingListWorkspaceContractTests
{



    /// <summary>新头行实体保留兼容字段退役约束，旧运行入口继续关闭。</summary>
    [TestMethod]
    public void TargetPersistence_RemovesLegacyPackingListCompatibilityColumns()
    {
        var entity = Read("SwiftExport.Core", "Entities", "Shipping", "PackingListHead.cs");
        var line = Read("SwiftExport.Core", "Entities", "Shipping", "PackingListLine.cs");
        Assert.IsFalse(entity.Contains("LegacyHeadId", StringComparison.Ordinal));
        Assert.IsFalse(entity.Contains("IsLegacyReadOnly", StringComparison.Ordinal));
        Assert.IsFalse(line.Contains("LegacyPackingListHeadId", StringComparison.Ordinal));
        Assert.IsFalse(File.Exists(Path.Combine(Root(), "SwiftExport.Infrastructure/Repositories/Shipping/PackingListDocumentRepository.cs")));
        Assert.IsFalse(File.Exists(Path.Combine(Root(), "SwiftExport.WebApi/Controllers/Shipping/PackingListWorkspaceController.cs")));
    }

    [TestMethod]
    public void SalesAndProcurementShippedFacts_UsePostedFinalOutbound()
    {
        var sales = Read("SwiftExport.Infrastructure", "Repositories", "Orders", "SalesOrderHeadRepository.cs");
        StringAssert.Contains(sales, "outHead.DocumentType=20 AND outHead.PostingStatus=1");
        Assert.IsFalse(sales.Contains("SUM(d.ShippedQty)", StringComparison.Ordinal));
        var procurement = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrderWorkspaceRepository.cs");
        StringAssert.Contains(procurement, "outHead.DocumentType=20 AND outHead.PostingStatus=1");
        StringAssert.Contains(procurement, "FinalShippedQty");
    }



    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));
    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。"); return string.Empty;
    }
}
