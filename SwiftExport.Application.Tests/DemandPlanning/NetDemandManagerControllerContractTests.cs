using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证物料需求管理与采购草稿 HTTP 边界保持当前路由及幂等职责。</summary>
[TestClass]
public sealed class NetDemandManagerControllerContractTests
{
    /// <summary>确认来源分组由物料需求 Manager 提供独立只读路由。</summary>
    /// <summary>确认采购创建允许同一业务命令重试，并由数据库幂等约束裁决。</summary>
    [TestMethod]
    public void SourceGroups_MustExposeDedicatedReadOnlyRoute()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerPath = Path.Combine(
            repositoryRoot,
            "SwiftExport.WebApi",
            "Controllers",
            "DemandPlanning",
            "Materials", "Query", "MaterialDemandQueryController.cs");
        var source = File.ReadAllText(controllerPath);

        StringAssert.Contains(source, "[HttpPost(\"material-source-groups\")]");
        StringAssert.Contains(source, "SearchMaterialSourceGroupsAsync");
        StringAssert.Contains(source, "MaterialDemandSourceGroupSearchRequest");
    }

    [TestMethod]
    public void CreatePurchaseDrafts_MustRelyOnDatabaseIdempotency_NotMemoryDuplicateFilter()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controllerPath = Path.Combine(
            repositoryRoot,
            "SwiftExport.WebApi",
            "Controllers",
            "Procurement",
            "PurchaseOrders",
            "Draft",
            "PurchaseDraftCreationController.cs");
        var source = File.ReadAllText(controllerPath);
        const string route = "[HttpPost(\"create\")]";
        var routeIndex = source.IndexOf(route, StringComparison.Ordinal);
        Assert.IsTrue(routeIndex >= 0, "未找到采购草稿 Create 路由。");

        var methodEnd = source.IndexOf(';', routeIndex);
        Assert.IsTrue(methodEnd > routeIndex, "无法定位采购草稿 Create action。");
        var actionSource = source.Substring(routeIndex, methodEnd - routeIndex + 1);

        Assert.IsFalse(
            actionSource.Contains("PreventDuplicateRequest", StringComparison.Ordinal),
            "Create 必须允许相同 ExecutionRequestId/CommandItemId 重试，由数据库幂等约束裁决。");
    }

    /// <summary>从测试输出目录向上定位仓库根目录。</summary>
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
