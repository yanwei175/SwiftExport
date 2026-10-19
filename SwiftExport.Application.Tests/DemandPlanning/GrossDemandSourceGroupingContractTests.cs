using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证成品需求分组和本体列表的筛选、分页及下游隔离契约。</summary>
[TestClass]
public sealed class GrossDemandSourceGroupingContractTests
{
    /// <summary>分组不受分解状态限制，指定来源的主列表保持不分页。</summary>
    [TestMethod]
    public void RepositoryQuery_MustKeepGroupingUnscopedAndSelectedSourceUnpaged()
    {
        var source = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "Sources", "Query", "PostgreSqlGrossDemandQueryRepository.Workspace.cs");

        StringAssert.Contains(source, "GrossDemandSourceGroupSearchRequest request");
        StringAssert.Contains(source, "SourceLifecycleStatuses");
        Assert.IsFalse(source.Contains("SourceDecompositionStatuses", StringComparison.Ordinal));
        StringAssert.Contains(source, "GROUP BY d.source_uid");
        StringAssert.Contains(source, "var sourceMode = request.SourceUid.HasValue");
        StringAssert.Contains(source, "var pagingSql = sourceMode");
        StringAssert.Contains(source, "LIMIT @PageSize OFFSET @Offset;");
        StringAssert.Contains(source, "PageSize = sourceMode ? Math.Max(1, total) : pageSize");
        Assert.IsFalse(source.Contains("10000", StringComparison.Ordinal));
    }

    /// <summary>成品需求列表只投影本体快照，不读取物料、采购、入库及交付事实。</summary>
    [TestMethod]
    public void RepositoryMainQuery_ProjectsFactsWithoutDownstreamEvidence()
    {
        var source = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "Sources", "Query", "PostgreSqlGrossDemandQueryRepository.Workspace.cs");
        var start = source.IndexOf("var selectSql = $$\"\"\"", StringComparison.Ordinal);
        var end = source.IndexOf("var countSql =", start, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start, "无法定位成品需求主列表 SQL。");
        var query = source[start..end];
        Assert.IsFalse(query.Contains("ExecutionProgress", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("ActiveNetDemandCount", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("CustomerSuppliedNetDemandCount", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("inbound_document", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("SUM(", StringComparison.OrdinalIgnoreCase));
        foreach(var retired in new[]{"HasActiveMaterial","HasValidPurchase","HasPostedDelivery","public.net_demand","public.purchase_order","public.packing_list","TraceView"})
            Assert.IsFalse(source.Contains(retired,StringComparison.Ordinal),retired);
        StringAssert.Contains(query, "d.source_version AS \"SourceVersion\"");
        StringAssert.Contains(query, "d.customer_id AS \"CustomerId\"");
        StringAssert.Contains(query, "d.unit_snapshot AS \"Unit\"");
        StringAssert.Contains(query, "d.description_snapshot AS \"Description\"");
    }

    [TestMethod]
    public void PublicApi_MustAddDedicatedGroupRouteWithoutChangingMainSearchResult()
    {
        var api = Read(
            "SwiftExport.Contracts",
            "Apis",
            "DemandPlanning",
            "Sources",
            "IGrossDemandWorkspaceApi.cs");
        var controller = Read(
            "SwiftExport.WebApi",
            "Controllers",
            "DemandPlanning",
            "Sources", "Query", "GrossDemandQueryController.cs");
        var manualController = Read(
            "SwiftExport.WebApi", "Controllers", "DemandPlanning",
            "Sources", "Manual", "ManualGrossDemandController.cs");

        StringAssert.Contains(api, "Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync");
        StringAssert.Contains(api, "[Post(\"/source-groups/search\")]");
        StringAssert.Contains(api, "Task<ServiceResult<List<GrossDemandSourceGroupDto>>> SearchSourceGroupsAsync");
        StringAssert.Contains(api, "GrossDemandSourceGroupSearchRequest request");
        StringAssert.Contains(controller, "[HttpPost(\"source-groups/search\")]");
        StringAssert.Contains(controller, "GrossDemandSourceGroupSearchRequest request");
        StringAssert.Contains(controller, "IGrossDemandQueryService queryService");
        StringAssert.Contains(manualController, "IManualGrossDemandService manualService");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                {
                    return directory.FullName;
                }
            }
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }
}
