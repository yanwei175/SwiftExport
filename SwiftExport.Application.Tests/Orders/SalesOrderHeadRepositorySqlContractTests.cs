using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Query;

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
        var where = Sql("CreateManagerWhere", new SalesOrderManagerSearchRequest { Keyword = "description" }, new PostgreSqlParameters());
        var projection = Sql("DetailLineProjection");
        StringAssert.Contains(where, "keyword_line.description_en COLLATE public.erp_dictionary_ci LIKE @Keyword");
        foreach (var column in new[] { "sku", "link_sku", "customer_sku", "description_en" })
            StringAssert.Contains(projection, "l.\"" + column + "\"");
        Assert.IsFalse((where + projection).Contains("default_description_en", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证销售查询直接采用来源需求与箱单净交付，不经 Allocation 或自动出库重复计数。
    /// </summary>
    [TestMethod]
    public void RepositorySql_MustNotUseAllocationOutboundCompatibilityChain()
    {
        var facts = Sql("CreateLineFactsSql");
        var page = Sql("CreateManagerPageSql");
        var shippingSql = facts + Sql("CreateManagerFactsSql") + page;
        // 仅检查交付数量链；独立采购追溯读取需求关联不属于重复计数。
        foreach (var table in new[] { "outbound_document_line", "outbound_document", "net_demand_allocation", "net_demand" })
            Assert.IsFalse(shippingSql.Contains("\"" + table + "\"", StringComparison.Ordinal), table);
        StringAssert.Contains(page, "f.shipped_qty END AS \"TotalShippedQty\"");
        StringAssert.Contains(page, "f.unshipped_qty END AS \"TotalUnshippedQty\"");
        var detail = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "SwiftExport.Infrastructure",
            "Repositories", "SalesOrders", "Query", "PostgreSqlSalesOrderQueryRepository.Detail.cs"));
        StringAssert.Contains(detail, "FreezeLineFactsAsync");
        StringAssert.Contains(detail, "facts.\"ShippedQty\",facts.\"UnshippedQty\"");
        StringAssert.Contains(facts, "FROM \"public\".\"gross_demand\" g");
        StringAssert.Contains(facts, "FROM \"public\".\"packing_list_line\" pl");
        StringAssert.Contains(facts, "ph.posting_status=@Posted");
        StringAssert.Contains(facts, "g.demand_basis_fingerprint");
    }

    /// <summary>调用当前 PG 的实际 SQL 生成方法，不建立会话或连接数据库。</summary>
    private static string Sql(string method, params object[] arguments)
        => (string)typeof(PostgreSqlSalesOrderQueryRepository).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments)!;

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
