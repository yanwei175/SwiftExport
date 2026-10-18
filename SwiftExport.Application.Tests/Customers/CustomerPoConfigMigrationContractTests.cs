using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Customers;

/// <summary>离线核对人工迁移与专库夹具的保全边界，不把文本检查称为实库执行证明。</summary>
[TestClass]
public sealed class CustomerPoConfigMigrationContractTests
{
    /// <summary>无效身份、唯一回填歧义、同 ID 重复及数值问题必须先阻断，不能通过删除或猜测身份完成升级。</summary>
    [TestMethod]
    public void Migration_ChecksAllCandidatesBeforePersistentChange()
    {
        var sql = Read("SwiftExport.Infrastructure/Database/PostgreSQL客户采购配置身份唯一迁移.sql");
        StringAssert.Contains(sql, "current_setting('swiftexport.expected_database'");
        StringAssert.Contains(sql, "inet_server_addr() IS DISTINCT FROM");
        StringAssert.Contains(sql, "cpc.customer_id IS NOT NULL AND c.uid=cpc.customer_id");
        StringAssert.Contains(sql, "cpc.supplier_id IS NULL AND");
        StringAssert.Contains(sql, "c.matches=1"); StringAssert.Contains(sql, "s.matches=1");
        var firstWrite = sql.IndexOf("UPDATE public.customer_po_config", StringComparison.Ordinal);
        Assert.IsTrue(firstWrite > sql.IndexOf("GROUP BY customer_id,supplier_id HAVING count(*)>1", StringComparison.Ordinal));
        Assert.IsTrue(firstWrite > sql.IndexOf("default_discount_rate>999.999999", StringComparison.Ordinal));
        Assert.IsFalse(Regex.IsMatch(sql, @"\bDELETE\s+FROM\b|\bTRUNCATE\b", RegexOptions.IgnoreCase));
        StringAssert.Contains(sql, "CREATE UNIQUE INDEX ux_customer_po_config_parties");
        StringAssert.Contains(sql, "version=cpc.version+1");
        StringAssert.Contains(sql, "LOCK TABLE public.customer_po_config IN ACCESS EXCLUSIVE MODE");
    }

    /// <summary>初始化拒绝非空库且只有四表，清理必须匹配专库标记和实例，发现额外对象即停止。</summary>
    [TestMethod]
    public void Fixture_InitAndCleanupAreLimitedToFreshMarkedDatabase()
    {
        var init = Read("SwiftExport.Application.Tests/Database/Fixtures/客户采购配置专库初始化.sql");
        var cleanup = Read("SwiftExport.Application.Tests/Database/Fixtures/客户采购配置专库清理.sql");
        Assert.AreEqual(4, Regex.Matches(init, "CREATE TABLE").Count);
        StringAssert.Contains(init, "专库非空，拒绝覆盖");
        foreach (var text in new[] { init, cleanup })
        {
            StringAssert.Contains(text, "dv_customer_po_config_tests_20261018");
            StringAssert.Contains(text, "SwiftExport.CustomerPoConfig.20261018.FreshFixture");
            StringAssert.Contains(text, "swiftexport.expected_server");
            Assert.IsFalse(Regex.IsMatch(text, @"\bDROP\s+DATABASE\b|\bTRUNCATE\b", RegexOptions.IgnoreCase));
        }
        StringAssert.Contains(cleanup, "发现本轮四表以外的对象，拒绝清理");
        StringAssert.Contains(cleanup, "DROP TABLE public.customer_po_config, public.sys_dic_currency, public.customer, public.supplier RESTRICT");
    }

    /// <summary>仅定位本检出目录，禁止将其他工作树文件当作验收依据。</summary>
    private static string Read(string relative)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SwiftExport-WebAPI.slnx"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root?.FullName ?? throw new DirectoryNotFoundException(), relative));
    }
}
