using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>验证库存迁移脚本保持只读预检、不可变流水和唯一投影约束。</summary>
[TestClass]
public sealed class InventoryImmutableLedgerSchemaContractTests
{
    /// <summary>预检不得执行表结构或数据写入。</summary>
    [TestMethod]
    public void Preflight_ShouldRemainReadOnly()
    {
        var sql = Read("SwiftExport.Infrastructure", "Migrations", "031_InventoryImmutableLedger_Preflight.sql");

        Assert.IsFalse(sql.Contains("ALTER TABLE", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("UPDATE dbo.", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sql.Contains("AS RowCount", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(4, Count(sql, "AS [RowCount]"));
        StringAssert.Contains(sql, "余额与流水净额不一致");
        StringAssert.Contains(sql, "余额投影已被软删除");
    }

    /// <summary>增量安装与新装脚本都必须建立不可变流水及反向、替代唯一关系。</summary>
    [TestMethod]
    public void Schema_ShouldProtectLedgerAndBalanceProjection()
    {
        var migration = Read("SwiftExport.Infrastructure", "Migrations", "031_InventoryImmutableLedger_AdditiveSchema.sql");
        var fresh = Read("SwiftExport.Infrastructure", "Database", "FreshInstall", "030_初始化目标业务表.sql");

        foreach (var sql in new[] { migration, fresh })
        {
            StringAssert.Contains(sql, "TR_InventoryLedgerEntry_Immutable");
            StringAssert.Contains(sql, "UX_InventoryLedger_ReversalOf");
            StringAssert.Contains(sql, "UX_InventoryLedger_ReplacementOf");
            StringAssert.Contains(sql, "CorrectionGroupId");
            StringAssert.Contains(sql, "SourceDocumentNoSnapshot");
            StringAssert.Contains(sql, "RequestFingerprint");
            StringAssert.Contains(sql, "WarehouseNameSnapshot nvarchar(300)");
        }
        StringAssert.Contains(migration, "CREATE UNIQUE INDEX UX_InventoryBalance_Warehouse_Product ON dbo.InventoryBalance(WarehouseSiteId,ProductId);");
        StringAssert.Contains(migration, "@WarehouseNameMaxLength NOT IN (400,600)");
        StringAssert.Contains(migration, "@WarehouseNameMaxLength=400");
        StringAssert.Contains(migration, "ALTER COLUMN WarehouseNameSnapshot nvarchar(300) NULL");
        StringAssert.Contains(migration, "InventoryBalance WHERE IsDeleted<>0");
    }

    /// <summary>隔离库夹具必须覆盖无列、旧 200 长度列和同一基线重复执行。</summary>
    [TestMethod]
    public void MigrationVerificationFixture_ShouldCoverBothBaselinesAndReruns()
    {
        var fixture = Read("SwiftExport.Infrastructure", "Migrations", "Verification", "031_InventoryImmutableLedger_MigrationVerification.sql");

        StringAssert.StartsWith(fixture, ":on error exit");
        StringAssert.Contains(fixture, "DB_NAME() NOT LIKE N'SwiftExportInventoryTest%'");
        StringAssert.Contains(fixture, "检测到同名表，已停止且不会删除");
        StringAssert.Contains(fixture, "必须先进入上述 Verification 目录");
        StringAssert.Contains(fixture, "WarehouseNameSnapshot nvarchar(200) NULL");
        Assert.AreEqual(4, Count(fixture, ":r ..\\031_InventoryImmutableLedger_AdditiveSchema.sql"));
        Assert.AreEqual(4, Count(fixture, ":r ..\\031_InventoryImmutableLedger_AdditiveSchema.sql\nGO"));
        StringAssert.Contains(fixture, "c.max_length=600");
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }

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
