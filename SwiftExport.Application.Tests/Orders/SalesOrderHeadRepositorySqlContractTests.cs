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
            "Orders",
            "SalesOrderHeadRepository.cs"));

        StringAssert.Contains(source, "keywordLine.DescriptionEn LIKE @Keyword");
        StringAssert.Contains(source, "l.Sku, l.LinkSku, l.CustomerSku, l.DescriptionEn");
        Assert.IsFalse(source.Contains("keywordLine.DefaultDescriptionEn", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("l.DefaultDescriptionEn", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证销售查询不再经 Allocation 反查出库，并在 PackingList 交付净额接入前返回可空占位。
    /// </summary>
    [TestMethod]
    public void RepositorySql_MustNotUseAllocationOutboundCompatibilityChain()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SwiftExport.Infrastructure",
            "Repositories",
            "Orders",
            "SalesOrderHeadRepository.cs"));

        Assert.IsFalse(source.Contains("OutboundDocumentLine", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("OutboundDocument", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("NetDemandAllocation", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("NetDemand", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("GrossDemand", StringComparison.Ordinal));
        StringAssert.Contains(source, "CAST(NULL AS decimal(18, 4)) AS TotalShippedQty");
        StringAssert.Contains(source, "CAST(NULL AS decimal(18, 4)) AS TotalUnshippedQty");
        StringAssert.Contains(source, "CAST(NULL AS decimal(18, 4)) AS ShippedQty");
        StringAssert.Contains(source, "CAST(NULL AS decimal(18, 4)) AS UnshippedQty");
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
