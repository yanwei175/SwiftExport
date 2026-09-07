using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.DemandPlanning;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>冻结来源需求交付 API、正式出库口径和事务锁边界。</summary>
[TestClass]
public sealed class SourceDemandDeliveryContractTests
{
    [TestMethod]
    public void RefitAndControllerExposeSameReadOnlyRoutes()
    {
        var methods = typeof(ISourceDemandDeliveryApi).GetMethods().ToDictionary(method => method.Name);
        Assert.AreEqual("/search", methods[nameof(ISourceDemandDeliveryApi.SearchAsync)]
            .GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().Single().Path);
        Assert.AreEqual("/{grossDemandId}", methods[nameof(ISourceDemandDeliveryApi.GetAsync)]
            .GetCustomAttributes(typeof(GetAttribute), false).Cast<GetAttribute>().Single().Path);

        var controller = Read("SwiftExport.WebApi", "Controllers", "DemandPlanning", "SourceDemandDeliveryController.cs");
        StringAssert.Contains(controller, "api/demand-planning/source-demands/delivery");
        Assert.IsFalse(controller.Contains("ReceiveAsync", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RepositoryUsesGrossDemandAndPostedFinalOutboundWithoutPlanningPrerequisite()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "GrossDemandRepository.cs");
        StringAssert.Contains(repository, "ol.GrossDemandId=d.Uid");
        StringAssert.Contains(repository, "oh.DocumentType=20 AND oh.PostingStatus=1");
        StringAssert.Contains(repository, "WITH(UPDLOCK,HOLDLOCK)");
        var shipmentStart = repository.IndexOf("private const string DeliveryShipmentApply", StringComparison.Ordinal);
        var shipmentEnd = repository.IndexOf("private const string DeliverySelect", shipmentStart + 1, StringComparison.Ordinal);
        Assert.IsTrue(shipmentStart >= 0 && shipmentEnd > shipmentStart);
        Assert.IsFalse(repository[shipmentStart..shipmentEnd].Contains("NetDemandAllocation", StringComparison.Ordinal));
    }

    [TestMethod]
    public void HistoricalMappingScriptsArePreviewFirstAndDoNotGuessBySku()
    {
        var preflight = Read("SwiftExport.Infrastructure", "Migrations", "025_SourceDemandDelivery_Preflight.sql");
        var migration = Read("SwiftExport.Infrastructure", "Migrations", "025_SourceDemandDelivery_AdditiveSchema.sql");
        Assert.IsFalse(preflight.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(preflight, "售后来源缺少显式订单池归属");
        StringAssert.Contains(preflight, "SuggestedCutoverSourceVersion");
        StringAssert.Contains(preflight, "该值不代表历史接收时点，需单独授权回填");
        StringAssert.Contains(preflight, "COL_LENGTH(N'dbo.PackingListContributionLine',N'SourceDemandVersionSnapshot')");
        StringAssert.Contains(migration, "EXEC sys.sp_executesql");
        StringAssert.Contains(migration, "INNER JOIN dbo.GrossDemand gd ON gd.Uid=pl.GrossDemandId");
        Assert.IsFalse(migration.Contains("SkuSnapshot=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TerminationAndPackingListSubmissionShareGrossDemandUpdateLock()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "GrossDemandRepository.cs");
        var reception = Read("SwiftExport.Application", "Services", "DemandPlanning", "SourceDemandReceptionService.cs");
        var packingList = Read("SwiftExport.Application", "Services", "Shipping", "PackingListWorkspaceService.cs");
        StringAssert.Contains(repository, "GetActiveBySourceForUpdateAsync");
        StringAssert.Contains(repository, "WITH (UPDLOCK, ROWLOCK)");
        StringAssert.Contains(repository, "FROM dbo.GrossDemand d WITH(UPDLOCK,HOLDLOCK)");
        StringAssert.Contains(reception, "ClosePendingAsync");
        StringAssert.Contains(packingList, "VerifySourceDeliveryAsync(snapshot, true");
        Assert.IsTrue(packingList.IndexOf("VerifySourceDeliveryAsync(snapshot, true", StringComparison.Ordinal)
                      < packingList.IndexOf("outboundPort.PostAsync", StringComparison.Ordinal));
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。");
        return string.Empty;
    }
}
