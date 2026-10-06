using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Document;
using System.Reflection;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>核对 PG 来源取消与物料历史分工，并验证实际映射后的历史 SQL 条件及括号。</summary>
[TestClass]
public sealed class SalesOrderCancellationSqlContractTests
{
    /// <summary>来源删除或作废不承担物料修订判定，历史评估仍由物料仓储负责。</summary>
    [TestMethod]
    public void RepositorySql_MustKeepSourceExitAndMaterialHistoryResponsibilitiesSeparated()
    {
        var sourceRepository = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "Sources", "Document", "PostgreSqlGrossDemandDocumentRepository.cs");
        var materialRepository = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "Materials", "Document", "PostgreSqlMaterialDemandDocumentRepository.cs");
        var hardDeleteSql = ExtractMethod(sourceRepository, "HardDeleteForSourceCancellationAsync");
        var voidSql = ExtractMethod(sourceRepository, "VoidRolledBackForSourceCancellationAsync");

        StringAssert.Contains(sourceRepository, "HardDeleteForSourceCancellationAsync");
        StringAssert.Contains(sourceRepository, "VoidRolledBackForSourceCancellationAsync");
        foreach (var sql in new[] { hardDeleteSql, voidSql })
        {
            Assert.IsFalse(sql.Contains("DecompositionRevision", StringComparison.Ordinal));
            Assert.IsFalse(sql.Contains("decomposition_revision", StringComparison.Ordinal));
            Assert.IsFalse(sql.Contains("net_demand", StringComparison.Ordinal));
        }
        StringAssert.Contains(materialRepository, "AssessSourceExitForUpdateAsync");
        StringAssert.Contains(materialRepository, "InGrossScope(ids");
        StringAssert.Contains(materialRepository, "FOR UPDATE;");
        Assert.IsFalse(materialRepository.Contains("net_demand_allocation", StringComparison.Ordinal));
    }

    /// <summary>展开当前映射及排序后检查历史状态、修订和括号，不执行数据库命令。</summary>
    [TestMethod]
    public void HistoryValidationSql_MustKeepSiblingExistsAndBalancedParentheses()
    {
        var source = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "Materials", "Document", "PostgreSqlMaterialDemandDocumentRepository.cs");
        var sql = ExtractSql(source, "AssessSourceExitForUpdateAsync");
        var compactSql = System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ");

        AssertParenthesesAreBalanced(sql);
        StringAssert.Contains(compactSql, "nd.\"lifecycle_status\" NOT IN (8,9)");
        StringAssert.Contains(compactSql, "nd.\"decomposition_revision\"<1");
        Assert.IsFalse(compactSql.Contains("net_demand_allocation", StringComparison.Ordinal));
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    /// <summary>提取既有 PG 历史 SQL，并用当前私有列/表/排序生成方法展开固定占位符，避免只验证 C# 模板的括号。</summary>
    private static string ExtractSql(string source, string methodName)
    {
        var methodStart = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, methodStart, $"找不到方法 {methodName}。");
        var sqlStartMarker = "var sql = $\"\"\"";
        var sqlStart = source.IndexOf(sqlStartMarker, methodStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, sqlStart, $"方法 {methodName} 中找不到 SQL 常量。");
        sqlStart += sqlStartMarker.Length;
        var sqlEnd = source.IndexOf("\"\"\";", sqlStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, sqlEnd, $"方法 {methodName} 中的 SQL 常量没有结束标记。");
        var repository = typeof(PostgreSqlMaterialDemandDocumentRepository);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var sql = source[sqlStart..sqlEnd];
        foreach (var table in new[] { "Table", "GrossTable" })
            sql = sql.Replace("{" + table + "}", (string)repository.GetField(table, flags)!.GetValue(null)!, StringComparison.Ordinal);
        var grossUid = (string)repository.GetMethod("G", flags)!.Invoke(null, ["Uid"])!;
        var order = (string)repository.GetMethod("UuidOrder", flags)!.Invoke(null, ["gd." + grossUid])!;
        sql = sql.Replace("{UuidOrder(\"gd.\" + G(\"Uid\"))}", order, StringComparison.Ordinal);
        sql = System.Text.RegularExpressions.Regex.Replace(sql, @"\{([CG])\(""([^""]+)""\)\}", match =>
            (string)repository.GetMethod(match.Groups[1].Value, flags)!.Invoke(null, [match.Groups[2].Value])!);
        Assert.IsFalse(sql.Contains('{'), "历史 SQL 尚有未展开的生成占位符。");
        return sql;
    }

    /// <summary>截取一个公开仓储方法的源码范围，供内联 SQL 职责断言使用。</summary>
    private static string ExtractMethod(string source, string methodName)
    {
        var methodStart = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, methodStart, $"找不到方法 {methodName}。");
        var methodEnd = source.IndexOf("\n    public ", methodStart + methodName.Length, StringComparison.Ordinal);
        return methodEnd < 0 ? source[methodStart..] : source[methodStart..methodEnd];
    }

    private static void AssertParenthesesAreBalanced(string sql)
    {
        var depth = 0;
        foreach (var character in sql)
        {
            if (character == '(')
            {
                depth++;
            }
            else if (character == ')')
            {
                depth--;
                Assert.IsGreaterThanOrEqualTo(0, depth, "SQL 中出现了没有左括号与之对应的右括号。");
            }
        }

        Assert.AreEqual(0, depth, "SQL 中存在未闭合的左括号。");
    }

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }
}
