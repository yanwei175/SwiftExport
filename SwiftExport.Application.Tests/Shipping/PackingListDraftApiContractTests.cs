using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
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

        var controller = Read("SwiftExport.WebApi", "Controllers", "Shipping", "PackingListController.cs");
        StringAssert.Contains(controller, "api/shipping/packing-lists");
        StringAssert.Contains(controller, "[Authorize]");
        StringAssert.Contains(controller, "PermissionKeys.ModuleShippingAccess");
        Assert.IsFalse(controller.Contains("PostAsync", StringComparison.Ordinal));
        Assert.IsFalse(controller.Contains("ReverseAsync", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Repository_UsesGrossDemandWithoutPlanningOrProcurementPrerequisites()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "PackingListRepository.cs");
        StringAssert.Contains(repository, "g.SourceType IN (N'SO',N'CR')");
        StringAssert.Contains(repository, "g.IsNonStandard=0");
        StringAssert.Contains(repository, "g.CustomerId IS NOT NULL AND g.ProductId IS NOT NULL");
        StringAssert.Contains(repository, "soh.Uid IS NOT NULL AND sol.Uid IS NOT NULL");
        StringAssert.Contains(repository, "h.PostingStatus=1");
        StringAssert.Contains(repository, "g.DemandQty-COALESCE(d.DeliveredQty,0)");
        StringAssert.Contains(repository, "pkg.PackageCount=1");
        foreach (var forbidden in new[] { "NetDemand", "Allocation", "PurchaseOrderHead", "PurchaseOrderLine", "PackingListDocument", "PackingListContributionLine" })
            Assert.IsFalse(repository.Contains(forbidden, StringComparison.Ordinal), forbidden);
    }

    [TestMethod]
    public void DraftPersistence_RequiresPositiveQuantityAndUniqueCreateCommand()
    {
        Assert.AreEqual("PL", DocumentTypeStaticsEnum.PackingList);
        Assert.AreEqual("IDEMPOTENCY_CONFLICT", PackingListErrorCodes.IdempotencyConflict);
        var migration = Read("SwiftExport.Infrastructure", "Migrations", "037_PackingList草稿接口_AdditiveSchema.sql");
        StringAssert.Contains(migration, "CHECK(Qty>0)");
        StringAssert.Contains(migration, "UX_PackingListHead_CreateCommand");
        var fresh = Read("SwiftExport.Infrastructure", "Database", "FreshInstall", "030_初始化目标业务表.sql");
        StringAssert.Contains(fresh, "CREATE TABLE dbo.PackingListHead");
        StringAssert.Contains(fresh, "CREATE TABLE dbo.PackingListLine");
        Assert.IsFalse(fresh.Contains("CREATE TABLE dbo.PackingListDocument", StringComparison.Ordinal));
        var warehouse = Read("SwiftExport.Infrastructure", "Repositories", "Inventory", "WarehouseSiteRepository.cs");
        StringAssert.Contains(warehouse, "dbo.PackingListLine");
        Assert.IsFalse(warehouse.Contains("dbo.PackingListContributionLine", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Service_KeepsCommandsAtomicAndDoesNotPostInventory()
    {
        var service = Read("SwiftExport.Application", "Services", "Shipping", "PackingListService.cs");
        StringAssert.Contains(service, "BeginTransaction(IsolationLevel.Serializable)");
        StringAssert.Contains(service, "LockCommandAsync");
        StringAssert.Contains(service, "BusinessOperationLog");
        StringAssert.Contains(service, "MidpointRounding.AwayFromZero");
        StringAssert.Contains(service, "LineSetMismatch");
        StringAssert.Contains(service, "PcsPerPackage = source.PcsPerPackage");
        Assert.IsFalse(service.Contains("line.PcsPerPackage = input.PcsPerPackage", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("IInventoryPostingService", StringComparison.Ordinal));
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
