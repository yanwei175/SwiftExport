using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>冻结统一交付查询、旧出库依赖移除和来源退出安全检查。</summary>
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
        StringAssert.Contains(controller, "[Authorize]");
        StringAssert.Contains(controller, "PermissionKeys.ModuleDemandPlanningAccess");
        Assert.IsFalse(controller.Contains("ReceiveAsync", StringComparison.Ordinal));
    }

    [TestMethod]
    public void UnifiedPackingListDeliveryProjectionIsRegistered()
    {
        var registration = Read("SwiftExport.Infrastructure", "DependencyInjection.cs");
        var delivery = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "SourceDeliveryQueryRepository.cs");
        var sourceRepository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "GrossDemandRepository.cs");
        StringAssert.Contains(registration, "ISourceDeliveryQueryRepository");
        StringAssert.Contains(delivery, "dbo.PackingListLine");
        StringAssert.Contains(delivery, "h.PostingStatus=@Posted");
        Assert.IsFalse(delivery.Contains("SourceType IN", StringComparison.Ordinal));
        Assert.IsFalse(sourceRepository.Contains("OutboundDocument", StringComparison.Ordinal));
    }

    /// <summary>共享契约支持非标来源，并且不再暴露库存池归属或库存动作判定。</summary>
    [TestMethod]
    public void ContractsExposeNonStandardIdentityWithoutOrderPoolFields()
    {
        Assert.AreEqual(typeof(Guid?), typeof(SourceDemandDeliveryItemDto).GetProperty("ProductId")!.PropertyType);
        Assert.AreEqual(typeof(Guid?), typeof(PackingListSourceCandidateDto).GetProperty("ProductId")!.PropertyType);
        Assert.AreEqual(typeof(Guid?), typeof(PackingListDraftLineInput).GetProperty("ProductId")!.PropertyType);
        Assert.IsNotNull(typeof(SourceDemandDeliveryItemDto).GetProperty("IsNonStandard"));
        Assert.IsNotNull(typeof(PackingListLineDto).GetProperty("SourceTypeSnapshot"));
        foreach (var removed in new[] { "OrderPoolSalesOrderId", "IsInventoryActionAllowed", "InventoryBlockerCode" })
            Assert.IsNull(typeof(SourceDemandDeliveryItemDto).GetProperty(removed), removed);
    }

    [TestMethod]
    public void SourceExitSeparatesMaterialHistoryFromShippingDeliveryProof()
    {
        var sourceRepository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "GrossDemandRepository.cs");
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "DemandPlanning", "NetDemandRepository.cs");
        var exitSql = repository.Split("AssessSourceExitForUpdateAsync")[1].Split("public async Task<IReadOnlyList<RepositoryWriteResult>>")[0];
        var material = Read("SwiftExport.Application", "Services", "DemandPlanning", "Materials", "SourceExit", "MaterialDemandSourceExitService.cs");
        var source = Read("SwiftExport.Application", "Services", "DemandPlanning", "Sources", "Reception", "SourceDemandReceptionService.cs");
        var shipping = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "PackingLists", "PackingListRepository.SourceExit.cs");
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
        var adapter = Read("SwiftExport.Application", "Services", "DemandPlanning", "Sources", "Delivery", "SourceDemandDeliveryService.cs");
        var sourceInterface = Read("SwiftExport.Application", "Interfaces", "DemandPlanning", "Sources", "Delivery", "ISourceDemandDeliveryService.cs");
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
