using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.DemandPlanning;

[TestClass]
public sealed class GrossDemandSourceGroupingContractTests
{
    [TestMethod]
    public void RepositoryQuery_MustKeepGroupingUnscopedAndSelectedSourceUnpaged()
    {
        var source = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "GrossDemandRepository.cs");

        StringAssert.Contains(source, "GrossDemandSourceGroupSearchRequest request");
        StringAssert.Contains(source, "SourceLifecycleStatuses");
        Assert.IsFalse(source.Contains("SourceDecompositionStatuses", StringComparison.Ordinal));
        StringAssert.Contains(source, "GROUP BY d.[SourceUid]");
        StringAssert.Contains(source, "var sourceMode = request.SourceUid.HasValue");
        StringAssert.Contains(source, "var pagingSql = sourceMode");
        StringAssert.Contains(source, "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;");
        StringAssert.Contains(source, "PageSize = sourceMode ? Math.Max(1, total) : pageSize");
        Assert.IsFalse(source.Contains("10000", StringComparison.Ordinal));
    }

    /// <summary>验证来源需求列表不再投影物料数量、采购收货或执行进度。</summary>
    [TestMethod]
    public void RepositoryMainQuery_MustNotCalculateDownstreamProgress()
    {
        var source = Read(
            "SwiftExport.Infrastructure",
            "Repositories",
            "DemandPlanning",
            "GrossDemandRepository.cs");
        var start = source.IndexOf("var selectSql = $@\"", StringComparison.Ordinal);
        var end = source.IndexOf("var countSql =", start, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start, "无法定位来源需求主列表 SQL。");
        var query = source[start..end];

        Assert.IsFalse(query.Contains("ExecutionProgress", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("ActiveNetDemandCount", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("CustomerSuppliedNetDemandCount", StringComparison.Ordinal));
        Assert.IsFalse(query.Contains("InboundDocument", StringComparison.Ordinal));
        StringAssert.Contains(query, "d.[SourceVersion], d.[CustomerId]");
        StringAssert.Contains(query, "d.[UnitSnapshot] AS [Unit]");
        StringAssert.Contains(query, "d.[DescriptionSnapshot] AS [Description]");
    }

    [TestMethod]
    public void PublicApi_MustAddDedicatedGroupRouteWithoutChangingMainSearchResult()
    {
        var api = Read(
            "SwiftExport.Contracts",
            "Apis",
            "DemandPlanning",
            "IGrossDemandWorkspaceApi.cs");
        var controller = Read(
            "SwiftExport.WebApi",
            "Controllers",
            "DemandPlanning",
            "GrossDemandWorkspaceController.cs");

        StringAssert.Contains(api, "Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync");
        StringAssert.Contains(api, "[Post(\"/source-groups/search\")]");
        StringAssert.Contains(api, "Task<ServiceResult<List<GrossDemandSourceGroupDto>>> SearchSourceGroupsAsync");
        StringAssert.Contains(api, "GrossDemandSourceGroupSearchRequest request");
        StringAssert.Contains(controller, "[HttpPost(\"source-groups/search\")]");
        StringAssert.Contains(controller, "GrossDemandSourceGroupSearchRequest request");
        StringAssert.Contains(controller, "IGrossDemandQueryService queryService");
        StringAssert.Contains(controller, "IManualGrossDemandService manualService");
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
