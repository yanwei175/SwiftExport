using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>
/// 验证销售订单仓储查询使用销售订单行的真实字段，避免列表筛选或详情加载依赖不存在的产品主数据字段。
/// </summary>
[TestClass]
public sealed class SalesOrderHeadRepositorySqlContractTests
{
    /// <summary>
    /// 验证关键词筛选和详情投影均读取 SalesOrderLine.DescriptionEn。
    /// </summary>
    [TestMethod]
    public void RepositorySql_MustUseSalesOrderLineDescriptionField()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SwiftExport.Infrastructure",
            "Repositories",
            "SalesOrders", "Query", "SalesOrderQueryRepository.cs"));

        StringAssert.Contains(source, "keywordLine.DescriptionEn LIKE @Keyword");
        StringAssert.Contains(source, "l.Sku, l.LinkSku, l.CustomerSku, l.DescriptionEn");
        Assert.IsFalse(source.Contains("keywordLine.DefaultDescriptionEn", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("l.DefaultDescriptionEn", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证销售查询直接采用来源需求与箱单净交付，不经 Allocation 或自动出库重复计数。
    /// </summary>
    [TestMethod]
    public void RepositorySql_MustNotUseAllocationOutboundCompatibilityChain()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SwiftExport.Infrastructure",
            "Repositories",
            "SalesOrders", "Query", "SalesOrderQueryRepository.cs"));

        Assert.IsFalse(source.Contains("OutboundDocumentLine", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("OutboundDocument", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("NetDemandAllocation", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("NetDemand", StringComparison.Ordinal));
        StringAssert.Contains(source, "SalesOrderFulfillmentQuerySql.CreateLineFacts");
        StringAssert.Contains(source, "f.ShippedQty END AS TotalShippedQty");
        StringAssert.Contains(source, "f.UnshippedQty END AS TotalUnshippedQty");
        StringAssert.Contains(source, "facts.ShippedQty, facts.UnshippedQty");
        var facts = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "SwiftExport.Infrastructure",
            "Repositories", "SalesOrders", "Query", "SalesOrderFulfillmentQuerySql.cs"));
        StringAssert.Contains(facts, "FROM dbo.GrossDemand g");
        StringAssert.Contains(facts, "FROM dbo.PackingListLine pl");
        StringAssert.Contains(facts, "ph.PostingStatus=@Posted");
        StringAssert.Contains(facts, "DemandBasisFingerprint");
    }

    /// <summary>
    /// 从测试运行目录向上定位仓库根目录，供源码契约检查使用。
    /// </summary>
    /// <returns>包含 AGENTS.md 的仓库根目录。</returns>
    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                {
                    return directory.FullName;
                }
            }
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }
}
