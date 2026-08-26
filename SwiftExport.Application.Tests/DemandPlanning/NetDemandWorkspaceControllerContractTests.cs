using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.DemandPlanning;

[TestClass]
public sealed class NetDemandWorkspaceControllerContractTests
{
    [TestMethod]
    public void SourceGroups_MustExposeDedicatedReadOnlyRoute()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerPath = Path.Combine(
            repositoryRoot,
            "SwiftExport.WebApi",
            "Controllers",
            "DemandPlanning",
            "NetDemandWorkspaceController.cs");
        var source = File.ReadAllText(controllerPath);

        StringAssert.Contains(source, "[HttpPost(\"source-groups\")]");
        StringAssert.Contains(source, "SearchSourceGroupsAsync");
        StringAssert.Contains(source, "NetDemandSourceGroupSearchRequest");
    }

    [TestMethod]
    public void CreatePurchaseDrafts_MustRelyOnDatabaseIdempotency_NotMemoryDuplicateFilter()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerPath = Path.Combine(
            repositoryRoot,
            "SwiftExport.WebApi",
            "Controllers",
            "DemandPlanning",
            "NetDemandWorkspaceController.cs");
        var source = File.ReadAllText(controllerPath);
        const string route = "[HttpPost(\"purchase-drafts/create\")]";
        var routeIndex = source.IndexOf(route, StringComparison.Ordinal);
        Assert.IsTrue(routeIndex >= 0, "未找到采购草稿 Create 路由。");

        var methodEnd = source.IndexOf(';', routeIndex);
        Assert.IsTrue(methodEnd > routeIndex, "无法定位采购草稿 Create action。");
        var actionSource = source.Substring(routeIndex, methodEnd - routeIndex + 1);

        Assert.IsFalse(
            actionSource.Contains("PreventDuplicateRequest", StringComparison.Ordinal),
            "Create 必须允许相同 ExecutionRequestId/CommandItemId 重试，由数据库幂等约束裁决。");
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                    && Directory.Exists(Path.Combine(directory.FullName, "SwiftExport.WebApi")))
                    return directory.FullName;
            }
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }
}
