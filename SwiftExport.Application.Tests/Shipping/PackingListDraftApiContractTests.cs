using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Interfaces.Inventory.Posting;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.StaticEnums;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>验证装箱单草稿 API、SQL 来源口径和部署保护保持一致。</summary>
[TestClass]
public sealed class PackingListDraftApiContractTests
{
    [TestMethod]
    public void RefitRoutes_MatchAuthorizedControllerBoundary()
    {
        var methods = typeof(IPackingListApi).GetMethods().ToDictionary(method => method.Name);
        Assert.AreEqual("/search", Attribute<PostAttribute>(methods[nameof(IPackingListApi.SearchAsync)]).Path);
        Assert.AreEqual("/{uid}", Attribute<GetAttribute>(methods[nameof(IPackingListApi.GetAsync)]).Path);
        Assert.AreEqual("/sources/search", Attribute<PostAttribute>(methods[nameof(IPackingListApi.SearchSourcesAsync)]).Path);
        Assert.AreEqual("/drafts", Attribute<PostAttribute>(methods[nameof(IPackingListApi.CreateAsync)]).Path);
        Assert.AreEqual("/drafts/{uid}", Attribute<PutAttribute>(methods[nameof(IPackingListApi.UpdateAsync)]).Path);
        Assert.AreEqual("/drafts/{uid}/delete", Attribute<PostAttribute>(methods[nameof(IPackingListApi.DeleteAsync)]).Path);

        var controller = Read("SwiftExport.WebApi", "Controllers", "Shipping", "PackingLists", "PackingListController.cs");
        StringAssert.Contains(controller, "api/shipping/packing-lists");
        StringAssert.Contains(controller, "[Authorize]");
        StringAssert.Contains(controller, "PermissionKeys.ModuleShippingAccess");
        // 第二阶段新增独立过账命令；继续保留原草稿路由和模块授权断言。
        StringAssert.Contains(controller, "PermissionKeys.PackingListPost");
        StringAssert.Contains(controller, "PermissionKeys.PackingListReverse");
    }

    [TestMethod]
    public void Repository_UsesGrossDemandWithoutPlanningOrProcurementPrerequisites()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "PackingLists", "PackingListRepository.Query.cs");
        var delivery = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "SourceDeliveryQueryRepository.cs");
        Assert.IsFalse(delivery.Contains("SourceType IN", StringComparison.Ordinal));
        StringAssert.Contains(delivery, "g.IsNonStandard=1 AND g.ProductId IS NULL");
        StringAssert.Contains(delivery, "h.PostingStatus=@Posted");
        StringAssert.Contains(delivery, "g.DemandQty-COALESCE(d.DeliveredQty,0)");
        StringAssert.Contains(delivery, "WITH (UPDLOCK,HOLDLOCK)");
        StringAssert.Contains(repository, "pkg.PackageCount=1");
        foreach (var forbidden in new[] { "NetDemand", "Allocation", "PurchaseOrderHead", "PurchaseOrderLine", "PackingListDocument", "PackingListContributionLine" })
            Assert.IsFalse(repository.Contains("dbo." + forbidden, StringComparison.Ordinal), forbidden);
    }

    [TestMethod]
    public void DraftPersistence_RequiresPositiveQuantityAndUniqueCreateCommand()
    {
        Assert.AreEqual("PL", DocumentTypeStaticsEnum.PackingList);
        Assert.AreEqual("COMMAND_CONFLICT", PackingListErrorCodes.CommandConflict);
        var baseline = Read("SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.Schema.sql");
        StringAssert.Contains(baseline, "CK_PackingListLine_Qty");
        StringAssert.Contains(baseline, "UX_PackingListHead_CreateCommand");
        StringAssert.Contains(baseline, "CREATE TABLE [dbo].[PackingListHead]");
        StringAssert.Contains(baseline, "CREATE TABLE [dbo].[PackingListLine]");
        Assert.IsFalse(baseline.Contains("PackingListDocument", StringComparison.Ordinal));
        var warehouse = Read("SwiftExport.Infrastructure", "Repositories", "Inventory", "Warehouses", "WarehouseManagementRepository.cs");
        // 箱单行已按确认方案移除仓库关联，实际库存仓库由流水保护，不能恢复旧行列依赖。
        foreach (var removed in new[] { "WarehouseSiteId", "WarehouseCodeSnapshot", "WarehouseNameSnapshot" })
            Assert.IsNull(typeof(SwiftExport.Core.Entities.Shipping.PackingListLine).GetProperty(removed));
        StringAssert.Contains(warehouse, "dbo.InventoryLedgerEntry WHERE WarehouseSiteId=@WarehouseSiteId");
        Assert.IsFalse(warehouse.Contains("dbo.PackingListLine", StringComparison.Ordinal));
        Assert.IsFalse(warehouse.Contains("dbo.PackingListContributionLine", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Service_KeepsCommandsAtomicAndDoesNotPostInventory()
    {
        var service = Read("SwiftExport.Application", "Services", "Shipping", "PackingLists", "PackingListDraftService.cs");
        StringAssert.Contains(service, "BusinessCommandExecutor executor");
        StringAssert.Contains(service, "token, IsolationLevel.Serializable");
        StringAssert.Contains(service, "CommandPipeline pipeline");
        StringAssert.Contains(service, "BusinessOperationLog");
        StringAssert.Contains(service, "MidpointRounding.AwayFromZero");
        StringAssert.Contains(service, "LineSetMismatch");
        var queries = Read("SwiftExport.Application", "Services", "Shipping", "PackingLists", "PackingListQueryService.cs");
        StringAssert.Contains(queries, "PcsPerPackage = source.IsNonStandard ? null : enrichment?.PcsPerPackage");
        StringAssert.Contains(service, "line.PcsPerPackage = input.PackingGroupId.HasValue ? input.Qty : input.PcsPerPackage");
        StringAssert.Contains(service, "line.PackageCount = input.PackingGroupId.HasValue ? 1 : CalculatePackageCount(input.Qty, input.PcsPerPackage)");
        Assert.IsFalse(service.Contains("IInventoryPostingService", StringComparison.Ordinal));
    }

    /// <summary>售后客户投诉号仅从客户和头行归属匹配的有效记录读取，保留销售 PO 取值。</summary>
    [TestMethod]
    public void AfterSalesComplaintNumberEnrichesCustomerPo()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "PackingLists", "PackingListRepository.Query.cs");
        StringAssert.Contains(repository, "THEN soh.CustomerPoNo");
        StringAssert.Contains(repository, "WHEN g.SourceType=N'CR' AND ash.Uid IS NOT NULL AND asl.Uid IS NOT NULL");
        StringAssert.Contains(repository, "THEN ash.CustomerComplaintNo END AS CustomerPoNo");
        StringAssert.Contains(repository, "ash.Uid=g.SourceUid");
        StringAssert.Contains(repository, "ash.CustomerId=g.CustomerId AND ash.IsDeleted=0");
        StringAssert.Contains(repository, "asl.Uid=g.SourceLineId");
        StringAssert.Contains(repository, "asl.HeaderId=ash.Uid AND asl.IsDeleted=0");
        var service = Read("SwiftExport.Application", "Services", "Shipping", "PackingLists", "PackingListDraftService.cs");
        var queries = Read("SwiftExport.Application", "Services", "Shipping", "PackingLists", "PackingListQueryService.cs");
        StringAssert.Contains(queries, "CustomerPoNo = enrichment?.CustomerPoNo");
        StringAssert.Contains(service, "CustomerPoNo = Clean(enrichment?.CustomerPoNo, 200)");
    }

    private static T Attribute<T>(System.Reflection.MethodInfo method) where T : Attribute =>
        method.GetCustomAttributes(typeof(T), false).Cast<T>().Single();

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
