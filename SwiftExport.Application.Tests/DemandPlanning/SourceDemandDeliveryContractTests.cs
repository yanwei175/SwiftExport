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
    public void ShippingRepositoryOwnsPostedFinalOutboundWithoutPlanningPrerequisite()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "SourceDeliveryRepository.cs");
        var formula = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "SourceDeliverySql.cs");
        var sourceRepository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "GrossDemandRepository.cs");
        var packingRepository = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "PackingListWorkspaceRepository.cs");
        StringAssert.Contains(formula, "line.GrossDemandId=g.Uid");
        StringAssert.Contains(formula, "document.DocumentType=20 AND document.PostingStatus=1");
        StringAssert.Contains(repository, "WITH(UPDLOCK,HOLDLOCK)");
        Assert.IsFalse(formula.Contains("NetDemandAllocation", StringComparison.Ordinal));
        Assert.IsFalse(sourceRepository.Contains("OutboundDocument", StringComparison.Ordinal));
        StringAssert.Contains(packingRepository, "SourceDeliverySql.FinalOutboundApply");
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
        var sourceRepository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "GrossDemandRepository.cs");
        var deliveryRepository = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "SourceDeliveryRepository.cs");
        var reception = Read("SwiftExport.Application", "Services", "DemandPlanning", "SourceDemandReceptionService.cs");
        var packingList = Read("SwiftExport.Application", "Services", "Shipping", "PackingListWorkspaceService.cs");
        StringAssert.Contains(sourceRepository, "GetActiveBySourceForUpdateAsync");
        StringAssert.Contains(sourceRepository, "WITH (UPDLOCK, ROWLOCK)");
        StringAssert.Contains(deliveryRepository, "FROM dbo.GrossDemand g WITH(UPDLOCK,HOLDLOCK)");
        StringAssert.Contains(reception, "ClosePendingAsync");
        StringAssert.Contains(packingList, "VerifySourceDeliveryAsync(snapshot, true");
        Assert.IsTrue(packingList.IndexOf("VerifySourceDeliveryAsync(snapshot, true", StringComparison.Ordinal)
                      < packingList.IndexOf("outboundPort.PostAsync", StringComparison.Ordinal));
    }

    /// <summary>旧来源 API 只能转发查询，PL 和售后必须直接依赖 Shipping 交付边界。</summary>
    [TestMethod]
    public void LegacySourceApiIsOneWayReadAdapter()
    {
        var adapter = Read("SwiftExport.Application", "Services", "DemandPlanning", "SourceDemandDeliveryService.cs");
        var sourceInterface = Read("SwiftExport.Application", "Interfaces", "DemandPlanning", "ISourceDemandDeliveryService.cs");
        var packingList = Read("SwiftExport.Application", "Services", "Shipping", "PackingListWorkspaceService.cs");
        var afterSales = Read("SwiftExport.Application", "Services", "QualityControl", "AfterSalesService.cs");
        StringAssert.Contains(adapter, "ISourceDeliveryService delivery");
        Assert.IsFalse(sourceInterface.Contains("VerifyForUpdateAsync", StringComparison.Ordinal));
        StringAssert.Contains(packingList, "ISourceDeliveryService sourceDelivery");
        StringAssert.Contains(afterSales, "ISourceDeliveryService delivery");
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
