using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>确保人工维护的空表头初始化脚本保留历史配置且索引语义正确。</summary>
[TestClass]
public sealed class ExcelFieldMappingSeedSqlTests
{
    /// <summary>采购、销售历史种子键保持唯一，且停用的空表头不进入唯一索引。</summary>
    [TestMethod]
    public void SeedSql_PreservesHistoricalKeysAndLeavesHeadersUnconfigured()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SwiftExport.slnx")))
            directory = directory.Parent;
        Assert.IsNotNull(directory);
        var path = Path.Combine(directory.FullName, "SwiftExport.Infrastructure", "Database", "Excel字段映射初始数据.sql");
        var sql = File.ReadAllText(path);
        var matches = Regex.Matches(sql, @"\(N'(PurchaseOrder|SalesOrder)', N'((?:Header|Line)\.[^']+)'\)");
        foreach (var group in new[] { "PurchaseOrder", "SalesOrder" })
        {
            var seeded = matches.Cast<Match>().Where(match => match.Groups[1].Value == group)
                .Select(match => match.Groups[2].Value).ToArray();
            Assert.IsTrue(seeded.Length > 0, group);
            Assert.AreEqual(seeded.Length, seeded.Distinct(StringComparer.Ordinal).Count(), group);
        }

        StringAssert.Contains(sql, "f.GroupCode, N'', N'', f.TargetFieldKey, 0");
        StringAssert.Contains(sql, "[NormalizedHeader] <> N''");
        StringAssert.Contains(sql, "CREATE UNIQUE INDEX [UX_ExcelFieldMapping_Group_Header]");
        StringAssert.Contains(sql, "SET QUOTED_IDENTIFIER ON;");
        StringAssert.Contains(sql, "NOT EXISTS");
    }
}
