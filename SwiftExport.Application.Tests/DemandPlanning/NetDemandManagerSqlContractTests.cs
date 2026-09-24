using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证物料需求 Manager 查询使用可复用的物化结果完成统计和分页。</summary>
[TestClass]
public sealed class NetDemandManagerSqlContractTests
{
    /// <summary>确认当前 Manager SQL 不退回一次性 CTE，也不重新依赖旧 Allocation。</summary>
    [TestMethod]
    public void ManagerQuery_MustMaterializeProjectionBeforeCountAndPage()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "Materials", "Query", "MaterialDemandQueryRepository.cs");
        var source = File.ReadAllText(sourcePath);

        StringAssert.Contains(source, "INTO #MaterialWorkspace");
        StringAssert.Contains(source, "SELECT COUNT(1) FROM #MaterialWorkspace");
        StringAssert.Contains(source, "FROM #MaterialWorkspace");
        StringAssert.Contains(source, "DROP TABLE #MaterialWorkspace");
        Assert.IsFalse(
            source.Contains("WITH MaterialWorkspace AS", StringComparison.Ordinal),
            "SQL Server CTE 只能服务紧随其后的一条语句，不能同时供统计和分页查询复用。");
        StringAssert.Contains(
            source,
            "COALESCE(NULLIF(LTRIM(RTRIM(p.[Unit])),N''),N'PCS') AS [Unit]");
        StringAssert.Contains(source, "!request.SourceUid.HasValue");
        StringAssert.Contains(source, "gd.[SourceUid]=@SelectedSourceUid");
        StringAssert.Contains(source, "SearchMaterialSourceGroupsAsync");
        StringAssert.Contains(source, "CreateMaterialWorkspaceWhere");
        Assert.IsFalse(source.Contains("[dbo].[NetDemandAllocation]", StringComparison.Ordinal));
    }

    /// <summary>客供件只读取自身产品主图，父件才允许优先使用来源图片快照。</summary>
    [TestMethod]
    public void ManagerQuery_ShouldResolveImagesByDemandRole()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "Materials", "Query", "MaterialDemandQueryRepository.cs");
        var source = File.ReadAllText(sourcePath);
        var managerQuery = source.Split("SearchManagerAsync", StringSplitOptions.None)[1]
            .Split("SearchMaterialSourceGroupsAsync", StringSplitOptions.None)[0];
        var candidateSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "Materials", "Sources",
            "MaterialDemandSourceRepository.cs"));
        var purchaseCandidateQuery = candidateSource.Split("GetPurchaseCandidatesAsync", StringSplitOptions.None)[1]
            .Split("PurchaseCandidatePurchaseFactsSql", StringSplitOptions.None)[0];

        StringAssert.Contains(managerQuery, "CASE WHEN nd.[DeliveryMode]=1");
        StringAssert.Contains(managerQuery, "THEN COALESCE(p.[MainImage],N'')");
        StringAssert.Contains(managerQuery,
            "ELSE COALESCE(NULLIF(LTRIM(RTRIM(gd.[ImagePathSnapshot])),N''),p.[MainImage],N'')");
        Assert.IsFalse(managerQuery.Contains(
            "COALESCE(gd.[ImagePathSnapshot],p.[MainImage]) AS [ImagePath]",
            StringComparison.Ordinal));
        StringAssert.Contains(purchaseCandidateQuery, "COALESCE(p.[MainImage],N'') AS [ImagePath]");
    }

    /// <summary>从测试输出目录向上定位仓库根目录。</summary>
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
