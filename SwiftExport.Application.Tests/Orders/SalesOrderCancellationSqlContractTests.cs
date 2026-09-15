using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Orders;

[TestClass]
public sealed class SalesOrderCancellationSqlContractTests
{
    [TestMethod]
    public void RepositorySql_MustKeepSourceExitAndMaterialHistoryResponsibilitiesSeparated()
    {
        var sourceRepository = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "GrossDemandRepository.cs");
        var materialRepository = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "NetDemandRepository.cs");
        var hardDeleteSql = ExtractMethod(sourceRepository, "HardDeleteForSourceCancellationAsync");
        var voidSql = ExtractMethod(sourceRepository, "VoidRolledBackForSourceCancellationAsync");

        StringAssert.Contains(sourceRepository, "HardDeleteForSourceCancellationAsync");
        StringAssert.Contains(sourceRepository, "VoidRolledBackForSourceCancellationAsync");
        Assert.IsFalse(hardDeleteSql.Contains("[DecompositionRevision]", StringComparison.Ordinal));
        Assert.IsFalse(hardDeleteSql.Contains("[dbo].[NetDemand]", StringComparison.Ordinal));
        Assert.IsFalse(voidSql.Contains("[DecompositionRevision]", StringComparison.Ordinal));
        Assert.IsFalse(voidSql.Contains("[dbo].[NetDemand]", StringComparison.Ordinal));
        StringAssert.Contains(materialRepository, "GetHistoryCountsForUpdateAsync");
        StringAssert.Contains(materialRepository, "HasInvalidExitHistoryAsync");
        StringAssert.Contains(materialRepository, "WITH (UPDLOCK,HOLDLOCK)");
        Assert.IsFalse(materialRepository.Contains("[dbo].[NetDemandAllocation]", StringComparison.Ordinal));
    }

    [TestMethod]
    public void HistoryValidationSql_MustKeepSiblingExistsAndBalancedParentheses()
    {
        var source = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "NetDemandRepository.cs");
        var sql = ExtractSql(source, "HasInvalidExitHistoryAsync");
        var compactSql = System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ");

        AssertParenthesesAreBalanced(sql);
        StringAssert.Contains(compactSql, "nd.[LifecycleStatus] NOT IN (@RolledBack,@Voided)");
        StringAssert.Contains(compactSql, "nd.[DecompositionRevision]<1");
        Assert.IsFalse(compactSql.Contains("[dbo].[NetDemandAllocation]", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CleanupMigration_MustDefaultToReadOnlyAndRejectBlockedBatch()
    {
        var script = Read(
            "SwiftExport.Infrastructure",
            "Migrations",
            "018_SalesOrderGrossDemandCancellationCleanup.sql");

        StringAssert.Contains(script, "DECLARE @Execute bit = 0");
        StringAssert.Contains(script, "IF @Execute = 0");
        StringAssert.Contains(script, "ROLLBACK TRANSACTION");
        StringAssert.Contains(script, "[Disposition]='BLOCKED'");
        StringAssert.Contains(script, "THROW 51001");
        StringAssert.Contains(script, "'HARD_DELETE'");
        StringAssert.Contains(script, "'RESTORE_VOID'");
        StringAssert.Contains(script, "COMMIT TRANSACTION");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string ExtractSql(string source, string methodName)
    {
        var methodStart = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, methodStart, $"找不到方法 {methodName}。");
        var sqlStartMarker = "const string sql = @\"";
        var sqlStart = source.IndexOf(sqlStartMarker, methodStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, sqlStart, $"方法 {methodName} 中找不到 SQL 常量。");
        sqlStart += sqlStartMarker.Length;
        var sqlEnd = source.IndexOf("\";", sqlStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, sqlEnd, $"方法 {methodName} 中的 SQL 常量没有结束标记。");
        return source[sqlStart..sqlEnd];
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
