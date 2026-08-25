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

        StringAssert.Contains(source, "BuildWorkspaceFilters(request, includeSourceUid: false)");
        StringAssert.Contains(source, "GROUP BY d.[SourceUid]");
        StringAssert.Contains(source, "var sourceMode = request.SourceUid.HasValue");
        StringAssert.Contains(source, "var pagingSql = sourceMode");
        StringAssert.Contains(source, "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;");
        StringAssert.Contains(source, "PageSize = sourceMode ? Math.Max(1, total) : pageSize");
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
        StringAssert.Contains(controller, "[HttpPost(\"source-groups/search\")]");
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
