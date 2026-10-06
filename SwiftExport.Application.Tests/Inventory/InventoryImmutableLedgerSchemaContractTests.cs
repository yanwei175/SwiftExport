using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>验证实体生成基线保持不可变流水和唯一投影约束。</summary>
[TestClass]
public sealed class InventoryImmutableLedgerSchemaContractTests
{
    /// <summary>PG 生成基线仅建结构，不得携带业务数据写入。</summary>
    [TestMethod]
    public void GeneratedSchema_ShouldNotContainBusinessDataWrites()
    {
        var sql = Baseline();
        Assert.IsFalse(sql.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(sql, @"\bUPDATE\s+(?:public|""public"")\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(sql, @"\bDELETE\s+FROM\s+(?:public|""public"")\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    /// <summary>PG 生成基线必须建立不可变流水及反向、替代唯一关系，快照存储类型保持。</summary>
    [TestMethod]
    public void Schema_ShouldProtectLedgerAndBalanceProjection()
    {
        var sql = Baseline();
        StringAssert.Contains(sql, "tr_inventory_ledger_entry_append_only");
        StringAssert.Contains(sql, "ux_inventory_ledger_reversal_of");
        StringAssert.Contains(sql, "ux_inventory_ledger_replacement_of");
        StringAssert.Contains(sql, "\"correction_group_id\"");
        StringAssert.Contains(sql, "\"source_document_no_snapshot\"");
        StringAssert.Contains(sql, "\"request_fingerprint\"");
        StringAssert.Contains(sql, "\"warehouse_name_snapshot\" varchar(300)");
        StringAssert.Contains(sql, "ux_inventory_balance_warehouse_product");
    }

    /// <summary>PG 序列和零外键规则必须随实体基线一并生成。</summary>
    [TestMethod]
    public void GeneratedSchema_ShouldDeclareSequenceAndNoForeignKey()
    {
        var sql = Baseline();
        StringAssert.Contains(sql, "CREATE SEQUENCE public.\"inventory_ledger_sequence\"");
        Assert.IsFalse(sql.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("REFERENCES", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>读取当前 PG 评审基线，保留脚本与实际部署验收的区别。</summary>
    private static string Baseline() => Read("SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.PostgreSql.Schema.preview.sql");

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。");
        return string.Empty;
    }
}
