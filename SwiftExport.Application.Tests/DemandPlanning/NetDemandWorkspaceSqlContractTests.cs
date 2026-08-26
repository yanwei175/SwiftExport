using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.DemandPlanning;

[TestClass]
public sealed class NetDemandWorkspaceSqlContractTests
{
    [TestMethod]
    public void WorkspaceQuery_MustMaterializeProjectionBeforeCountAndPage()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "NetDemandRepository.Workspace.cs");
        var source = File.ReadAllText(sourcePath);

        StringAssert.Contains(source, "INTO #Workspace");
        StringAssert.Contains(source, "SELECT COUNT(1) FROM #Workspace");
        StringAssert.Contains(source, "SELECT w.* FROM #Workspace");
        StringAssert.Contains(source, "DROP TABLE #Workspace");
        Assert.IsFalse(
            source.Contains("WITH Workspace AS", StringComparison.Ordinal),
            "SQL Server CTE 只能服务紧随其后的一条语句，不能同时供统计和分页查询复用。");
        StringAssert.Contains(
            source,
            "COALESCE(NULLIF(LTRIM(RTRIM(p.[Unit])),N''),N'PCS') AS [Unit]");
        StringAssert.Contains(source, "!request.SourceUid.HasValue");
        StringAssert.Contains(source, "gd.[SourceUid]=@SelectedSourceUid");
        StringAssert.Contains(source, "SearchSourceGroupsAsync");
        StringAssert.Contains(source, "PlanStatuses = request.PlanStatuses");
    }

    private static string FindRepositoryRoot()
    {
        foreach (var startDirectory in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                    && Directory.Exists(Path.Combine(directory.FullName, "SwiftExport.Infrastructure")))
                {
                    return directory.FullName;
                }
            }
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录，净需求查询 SQL 契约测试无法执行。");
        return string.Empty;
    }
}
