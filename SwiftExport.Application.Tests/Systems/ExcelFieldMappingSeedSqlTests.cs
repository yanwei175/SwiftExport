using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Systems.ExcelFieldMappings;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>确保人工维护的空表头初始化脚本与后端字段目录和索引语义一致。</summary>
[TestClass]
public sealed class ExcelFieldMappingSeedSqlTests
{
    /// <summary>采购、销售种子覆盖代码白名单，且停用的空表头不进入唯一索引。</summary>
    [TestMethod]
    public void SeedSql_MatchesCatalogAndLeavesHeadersUnconfigured()
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
            CollectionAssert.AreEquivalent(ExcelFieldCatalog.GetFields(group).Select(field => field.FieldKey).ToArray(), seeded, group);
        }

        StringAssert.Contains(sql, "f.GroupCode, N'', N'', f.TargetFieldKey, 0");
        StringAssert.Contains(sql, "[NormalizedHeader] <> N''");
        StringAssert.Contains(sql, "SET QUOTED_IDENTIFIER ON;");
        StringAssert.Contains(sql, "NOT EXISTS");
    }
}
