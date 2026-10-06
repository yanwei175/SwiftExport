using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证物料需求 Manager 查询使用可复用的物化结果完成统计和分页。</summary>
[TestClass]
public sealed class NetDemandManagerSqlContractTests
{
    /// <summary>PG 用单条命令的物化结果同时完成统计和分页，不重复读取业务事实或依赖旧 Allocation。</summary>
    [TestMethod]
    public void ManagerQuery_MustMaterializeProjectionBeforeCountAndPage()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "Materials", "Query", "PostgreSqlMaterialDemandQueryRepository.cs");
        var source = File.ReadAllText(sourcePath);

        StringAssert.Contains(source, "WITH workspace AS MATERIALIZED (");
        StringAssert.Contains(source, "SELECT COUNT(*)::integer FROM filtered");
        StringAssert.Contains(source, "FROM workspace WHERE {quickWhere}");
        StringAssert.Contains(source, "SELECT {WorkspaceColumns} FROM filtered");
        StringAssert.Contains(source, "FROM page), '[]'::jsonb)::text AS \"ItemsJson\"");
        StringAssert.Contains(source, "QuerySingleAsync<WorkspaceQueryRow>(Command(sql, parameters, cancellationToken))");
        Assert.IsFalse(
            source.Contains("#MaterialWorkspace", StringComparison.Ordinal),
            "PG 物化结果在同一命令中复用并结束，不持有 SQL Server 临时表资源。");
        StringAssert.Contains(source, "unitFacts.unit AS \"Unit\"");
        StringAssert.Contains(source, "WHEN unitFacts.unit IS NULL THEN '物料单位需核实'");
        StringAssert.Contains(source, "!request.SourceUid.HasValue");
        StringAssert.Contains(source, "gd.source_uid=@SelectedSourceUid");
        StringAssert.Contains(source, "SearchMaterialSourceGroupsAsync");
        StringAssert.Contains(source, "CreateWorkspaceWhere");
        Assert.IsFalse(source.Contains("net_demand_allocation", StringComparison.Ordinal));
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
            "Materials", "Query", "PostgreSqlMaterialDemandQueryRepository.cs");
        var source = File.ReadAllText(sourcePath);
        var managerQuery = source.Split("SearchManagerAsync", StringSplitOptions.None)[1]
            .Split("SearchMaterialSourceGroupsAsync", StringSplitOptions.None)[0];
        var candidateSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "Materials", "Sources",
            "PostgreSqlMaterialDemandSourceRepository.cs"));
        var purchaseCandidateQuery = candidateSource.Split("GetPurchaseCandidatesAsync", StringSplitOptions.None)[1]
            .Split("private static string PurchaseFactsSql", StringSplitOptions.None)[0];

        StringAssert.Contains(managerQuery, "CASE WHEN nd.delivery_mode=1");
        StringAssert.Contains(managerQuery, "THEN COALESCE(p.main_image,'')");
        StringAssert.Contains(managerQuery,
            "ELSE COALESCE(NULLIF(btrim(gd.image_path_snapshot, ' '),''),p.main_image,'')");
        Assert.IsFalse(managerQuery.Contains(
            "COALESCE(gd.image_path_snapshot,p.main_image) AS \"ImagePath\"",
            StringComparison.Ordinal));
        StringAssert.Contains(purchaseCandidateQuery, "COALESCE(p.main_image, '') AS \"ImagePath\"");
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
