using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>验证实体生成基线保持不可变流水和唯一投影约束。</summary>
[TestClass]
public sealed class InventoryImmutableLedgerSchemaContractTests
{
    /// <summary>生成基线仅建结构，不得携带业务数据写入。</summary>
    [TestMethod]
    public void GeneratedSchema_ShouldNotContainBusinessDataWrites()
    {
        var sql = Baseline();
        Assert.IsFalse(sql.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("UPDATE dbo.", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("DELETE FROM dbo.", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>生成基线必须建立不可变流水及反向、替代唯一关系。</summary>
    [TestMethod]
    public void Schema_ShouldProtectLedgerAndBalanceProjection()
    {
        var sql = Baseline();
        StringAssert.Contains(sql, "TR_InventoryLedgerEntry_AppendOnly");
        StringAssert.Contains(sql, "UX_InventoryLedger_ReversalOf");
        StringAssert.Contains(sql, "UX_InventoryLedger_ReplacementOf");
        StringAssert.Contains(sql, "[CorrectionGroupId]");
        StringAssert.Contains(sql, "[SourceDocumentNoSnapshot]");
        StringAssert.Contains(sql, "[RequestFingerprint]");
        StringAssert.Contains(sql, "[WarehouseNameSnapshot] nvarchar(300)");
        StringAssert.Contains(sql, "UX_InventoryBalance_Warehouse_Product");
    }

    /// <summary>序列和零外键规则必须随实体基线一并生成。</summary>
    [TestMethod]
    public void GeneratedSchema_ShouldDeclareSequenceAndNoForeignKey()
    {
        var sql = Baseline();
        StringAssert.Contains(sql, "CREATE SEQUENCE [dbo].[InventoryLedgerSequence]");
        Assert.IsFalse(sql.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("REFERENCES", StringComparison.OrdinalIgnoreCase));
    }

    private static string Baseline() => Read("SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.Schema.sql");

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
