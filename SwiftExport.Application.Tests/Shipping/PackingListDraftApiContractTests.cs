using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Interfaces.Inventory.Posting;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Infrastructure.Repositories.Shipping;

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
        StringAssert.Contains(controller, "Shipping.PackingList.View");
        StringAssert.Contains(controller, "Shipping.PackingList.Create");
        StringAssert.Contains(controller, "Shipping.PackingList.Edit");
        StringAssert.Contains(controller, "Shipping.PackingList.Delete");
        // 草稿与过账、反向分别授权，读取权限不能隐式授予写入。
        StringAssert.Contains(controller, "PermissionKeys.PackingListPost");
        StringAssert.Contains(controller, "PermissionKeys.PackingListReverse");
    }

    /// <summary>PG 草稿读取只依赖原毛需求和正式交付，仅采用有效外箱且不增加采购前置。</summary>
    [TestMethod]
    public void Repository_UsesGrossDemandWithoutPlanningOrProcurementPrerequisites()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "PackingLists", "PostgreSqlPackingListRepository.cs");
        var delivery = PostgreSqlSourceDeliveryQueryRepository.Projection(true);
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(delivery, "(?:\"source_type\"|\"SourceType\")\\s+IN\\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        StringAssert.Contains(delivery, "g.\"is_non_standard\"=TRUE AND g.\"product_id\" IS NULL");
        StringAssert.Contains(delivery, "h.\"posting_status\"=@Posted");
        StringAssert.Contains(delivery, "g.\"demand_qty\"-COALESCE(d.\"DeliveredQty\",0)");
        StringAssert.Contains(delivery, "FOR UPDATE OF l,h");
        StringAssert.Contains(repository, "pkg.package_count=1");
        StringAssert.Contains(repository, "pp.is_deleted=FALSE AND pp.package_type=@CartonPackageType");
        Assert.IsFalse(repository.Contains("is_default", StringComparison.Ordinal));
        foreach (var forbidden in new[] { "net_demand", "allocation", "purchase_order_head", "purchase_order_line", "packing_list_document", "packing_list_contribution_line" })
            Assert.IsFalse(repository.Contains("public." + forbidden, StringComparison.Ordinal), forbidden);
    }

    /// <summary>PG 基线保持原数量和创建幂等约束，仓库生命周期只能依赖余额和流水。</summary>
    [TestMethod]
    public void DraftPersistence_RequiresPositiveQuantityAndUniqueCreateCommand()
    {
        Assert.AreEqual("PL", DocumentTypeStaticsEnum.PackingList);
        Assert.AreEqual("COMMAND_CONFLICT", PackingListErrorCodes.CommandConflict);
        var baseline = Read("SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.PostgreSql.Schema.preview.sql");
        StringAssert.Contains(baseline, "ck_packing_list_line_qty");
        StringAssert.Contains(baseline, "ux_packing_list_head_create_command");
        StringAssert.Contains(baseline, "CREATE TABLE \"public\".\"packing_list_head\"");
        StringAssert.Contains(baseline, "CREATE TABLE \"public\".\"packing_list_line\"");
        Assert.IsFalse(baseline.Contains("packing_list_document", StringComparison.Ordinal));
        var warehouse = Read("SwiftExport.Infrastructure", "Repositories", "Inventory", "Warehouses", "PostgreSqlWarehouseManagementRepository.cs");
        // 箱单行已按确认方案移除仓库关联，实际库存仓库由流水保护，不能恢复旧行列依赖。
        foreach (var removed in new[] { "WarehouseSiteId", "WarehouseCodeSnapshot", "WarehouseNameSnapshot" })
            Assert.IsNull(typeof(SwiftExport.Core.Entities.Shipping.PackingListLine).GetProperty(removed));
        StringAssert.Contains(warehouse, "public.inventory_ledger_entry WHERE warehouse_site_id=@WarehouseSiteId");
        Assert.IsFalse(warehouse.Contains("public.packing_list_line", StringComparison.Ordinal));
        Assert.IsFalse(warehouse.Contains("public.packing_list_contribution_line", StringComparison.Ordinal));
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
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Shipping", "PackingLists", "PostgreSqlPackingListRepository.cs");
        StringAssert.Contains(repository, "THEN soh.customer_po_no");
        StringAssert.Contains(repository, "WHEN rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='CR' AND ash.uid IS NOT NULL AND asl.uid IS NOT NULL");
        StringAssert.Contains(repository, "THEN ash.customer_complaint_no END AS \"CustomerPoNo\"");
        StringAssert.Contains(repository, "ash.uid=g.source_uid");
        StringAssert.Contains(repository, "ash.customer_id=g.customer_id AND ash.is_deleted=FALSE");
        StringAssert.Contains(repository, "asl.uid=g.source_line_id");
        StringAssert.Contains(repository, "asl.header_id=ash.uid AND asl.is_deleted=FALSE");
        var service = Read("SwiftExport.Application", "Services", "Shipping", "PackingLists", "PackingListDraftService.cs");
        var queries = Read("SwiftExport.Application", "Services", "Shipping", "PackingLists", "PackingListQueryService.cs");
        StringAssert.Contains(queries, "CustomerPoNo = enrichment?.CustomerPoNo");
        StringAssert.Contains(service, "CustomerPoNo = Clean(input.CustomerPoNo ?? enrichment?.CustomerPoNo, 200)");
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
