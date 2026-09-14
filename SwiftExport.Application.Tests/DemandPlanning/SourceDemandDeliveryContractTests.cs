using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.DemandPlanning;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>冻结交付未接入边界、旧出库依赖移除和来源退出安全检查。</summary>
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
    public void OldOutboundProjectionAndRepositoryAreRemoved()
    {
        Assert.IsFalse(File.Exists(Path.Combine(Root(), "SwiftExport.Infrastructure/Repositories/Shipping/SourceDeliveryRepository.cs")));
        Assert.IsFalse(File.Exists(Path.Combine(Root(), "SwiftExport.Infrastructure/Repositories/Shipping/SourceDeliverySql.cs")));
        Assert.IsFalse(File.Exists(Path.Combine(Root(), "SwiftExport.Core/Interfaces/Shipping/ISourceDeliveryRepository.cs")));
        var registration = Read("SwiftExport.Infrastructure", "DependencyInjection.cs");
        var sourceRepository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "GrossDemandRepository.cs");
        Assert.IsFalse(registration.Contains("ISourceDeliveryRepository", StringComparison.Ordinal));
        Assert.IsFalse(sourceRepository.Contains("OutboundDocument", StringComparison.Ordinal));
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
    public void SourceExitUsesActualSupplyAndDoesNotTreatMissingDeliveryAsSafe()
    {
        var sourceRepository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "GrossDemandRepository.cs");
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "NetDemandRepository.cs");
        var exitSql = repository.Split("public Task<bool> HasSourceExitBlockerAsync")[1].Split("public async Task<IReadOnlyDictionary<Guid, int>>")[0];
        var material = Read("SwiftExport.Application", "Services", "DemandPlanning", "MaterialDemandEstablishmentService.cs");
        StringAssert.Contains(sourceRepository, "GetActiveBySourceForUpdateAsync");
        StringAssert.Contains(sourceRepository, "WITH (UPDLOCK, ROWLOCK)");
        foreach (var forbidden in new[] { "OutboundDocument", "PackingListContributionLine", "NetDemandAllocation", "il.[GrossDemandId]", "il.[DemandExecutionLinkId]" })
            Assert.IsFalse(exitSql.Contains(forbidden, StringComparison.Ordinal), forbidden);
        StringAssert.Contains(exitSql, "po.[Uid]=il.[SourceLineId]");
        StringAssert.Contains(exitSql, "link.[TargetHeadId]=po.[PurchaseOrderId]");
        StringAssert.Contains(exitSql, "link.[TargetLineId]=po.[Uid]");
        StringAssert.Contains(exitSql, "ih.[DocumentType]=10");
        StringAssert.Contains(exitSql, "po.[Uid] IS NULL");
        StringAssert.Contains(exitSql, "d.[SourceType]=@SourceType AND d.[SourceUid]=@SourceUid AND d.[SourceLineId] IN @LineIds");
        StringAssert.Contains(material, "SOURCE_EXIT_SAFETY_UNAVAILABLE");
    }

    /// <summary>旧来源 API 只能转发查询；售后结案不得依赖按库存出库行聚合的通用交付边界。</summary>
    [TestMethod]
    public void LegacySourceApiIsOneWayReadAdapter()
    {
        var adapter = Read("SwiftExport.Application", "Services", "DemandPlanning", "SourceDemandDeliveryService.cs");
        var sourceInterface = Read("SwiftExport.Application", "Interfaces", "DemandPlanning", "ISourceDemandDeliveryService.cs");
        var afterSales = Read("SwiftExport.Application", "Services", "QualityControl", "AfterSalesService.cs");
        StringAssert.Contains(adapter, "ISourceDeliveryService delivery");
        Assert.IsFalse(sourceInterface.Contains("VerifyForUpdateAsync", StringComparison.Ordinal));
        Assert.IsFalse(afterSales.Contains("ISourceDeliveryService", StringComparison.Ordinal));
        Assert.IsFalse(afterSales.Contains("VerifyClosureAsync", StringComparison.Ordinal));
        StringAssert.Contains(afterSales, "AFTERSALES_DELIVERY_QUERY_UNAVAILABLE");
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
