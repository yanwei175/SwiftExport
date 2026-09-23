using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>检查入库 SQL 的边界及关键来源口径，不冒充真实 SQL Server 并发测试。</summary>
[TestClass]
public sealed class InboundPostingSqlContractTests
{
    /// <summary>草稿提示排除本单且不混入正式净额，来源锁在入库锁之前。</summary>
    [TestMethod]
    public void Sql_SeparatesDraftWarningsAndFormalCapacity()
    {
        var root = FindRoot();
        var query = File.ReadAllText(Path.Combine(root, "SwiftExport.Infrastructure/Repositories/Inventory/InboundManagerRepository.cs"));
        StringAssert.Contains(query, "dh.Uid<>@Uid");
        StringAssert.Contains(query, "dh.Uid<>@CurrentInboundDocumentId");
        StringAssert.Contains(query, "dh.PostingStatus=0 AND dh.LifecycleStatus=0");
        StringAssert.Contains(query, "rh.PostingStatus IN (1,8)");
        StringAssert.Contains(query, "COUNT(DISTINCT dh.Uid)");
        var service = File.ReadAllText(Path.Combine(root, "SwiftExport.Application/Services/Inventory/Inbounds/InboundPostingService.cs"));
        Assert.IsTrue(service.IndexOf("LockPurchaseSourcesAsync", StringComparison.Ordinal) < service.IndexOf("LockHeaderAsync", StringComparison.Ordinal));
        StringAssert.Contains(service, "inventoryPosting.PostAsync");
        var posting = File.ReadAllText(Path.Combine(root, "SwiftExport.Infrastructure/Repositories/Inventory/InboundManagerRepository.Posting.cs"));
        Assert.IsFalse(posting.Contains("UPDATE dbo.InventoryBalance", StringComparison.Ordinal));
        Assert.IsFalse(posting.Contains("INSERT dbo.InventoryLedgerEntry", StringComparison.Ordinal));
        StringAssert.Contains(posting, "dbo.PurchaseOrderHead WITH(UPDLOCK,HOLDLOCK)");
        StringAssert.Contains(posting, "dbo.PurchaseOrderLine WITH(UPDLOCK,HOLDLOCK)");
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "SwiftExport.Application"))) return dir.FullName;
        throw new DirectoryNotFoundException("未找到测试源码根目录。");
    }
}
