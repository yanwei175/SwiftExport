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
    public void SourceExitSeparatesMaterialHistoryFromShippingDeliveryProof()
    {
        var sourceRepository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "GrossDemandRepository.cs");
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "NetDemandRepository.cs");
        var exitSql = repository.Split("AssessSourceExitForUpdateAsync")[1].Split("public async Task<IReadOnlyList<RepositoryWriteResult>>")[0];
        var material = Read("SwiftExport.Application", "Services", "DemandPlanning", "MaterialDemandEstablishmentService.cs");
        var source = Read("SwiftExport.Application", "Services", "DemandPlanning", "SourceDemandReceptionService.cs");
        var shipping = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "PackingListRepository.cs");
        StringAssert.Contains(sourceRepository, "GetActiveBySourceForUpdateAsync");
        StringAssert.Contains(sourceRepository, "WITH (UPDLOCK, HOLDLOCK, ROWLOCK)");
        foreach (var forbidden in new[] { "DemandExecutionLink", "PurchaseOrder", "InboundDocument", "OutboundDocument", "PackingList" })
            Assert.IsFalse(exitSql.Contains(forbidden, StringComparison.Ordinal), forbidden);
        StringAssert.Contains(exitSql, "LifecycleStatus] NOT IN (@RolledBack,@Voided)");
        StringAssert.Contains(material, "AssessSourceExitAsync");
        StringAssert.Contains(source, "VerifySourceExitAsync");
        StringAssert.Contains(shipping, "ActiveDraftCount");
        StringAssert.Contains(shipping, "PostedPackingListCount");
        StringAssert.Contains(shipping, "PostedCustomerOutboundCount");
        StringAssert.Contains(shipping, "o.TriggerPackingListId");
        StringAssert.Contains(shipping, "o.DocumentType=@FinalCustomerShipment");
        Assert.IsFalse(shipping.Contains("StockIssue", StringComparison.Ordinal));
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
