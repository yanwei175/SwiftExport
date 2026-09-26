using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>冻结采购管理新链查询的路由、聚合来源与旧链隔离边界。</summary>
[TestClass]
public sealed class PurchaseOrderManagerQueryContractTests
{
    /// <summary>确认管理接口只声明查询路由，快速命令仍复用既有订单控制器。</summary>
    [TestMethod]
    public void Controller_MustExposeManagerQueriesWithoutDuplicatingCommands()
    {
        var manager = Read("SwiftExport.WebApi", "Controllers", "Procurement", "PurchaseOrderManagerController.cs");
        var orders = Read("SwiftExport.WebApi", "Controllers", "Procurement", "ProcurementOrderController.cs");

        StringAssert.Contains(manager, "api/procurement/order-manager");
        StringAssert.Contains(manager, "[HttpPost(\"search\")]");
        StringAssert.Contains(manager, "[HttpGet(\"{purchaseOrderId:guid}/lines\")]");
        Assert.IsFalse(manager.Contains("confirm", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(orders, "[HttpGet(\"{uid}/confirmation-context\")]");
        StringAssert.Contains(orders, "[HttpPost(\"{uid}/confirm\")]");
        StringAssert.Contains(orders, "[HttpPost(\"{uid}/cancel-confirmation\")]");
    }

    /// <summary>确认头与行投影使用冻结的权威事实，且状态筛选保持集合 IN 语义。</summary>
    [TestMethod]
    public void RepositorySql_MustUseLatestProgressTraceableReceiptFactsAndMultiStatusFilters()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrderManagerQueryRepository.cs");

        Assert.IsFalse(repository.Contains("ExecutionModel", StringComparison.Ordinal));
        StringAssert.Contains(repository, "h.ConfirmationStatus IN @ConfirmationStatuses");
        StringAssert.Contains(repository, "h.LifecycleStatus IN @LifecycleStatuses");
        StringAssert.Contains(repository, "h.CustomerCode AS CustomerCode");
        StringAssert.Contains(repository, "OR h.CustomerCode LIKE @Keyword");
        StringAssert.Contains(repository, "GROUP BY h.Uid,h.Version,h.LifecycleStatus,h.OrderNo,h.BoNo,h.CustomerCode");
        StringAssert.Contains(repository, "SELECT TOP (1)");
        StringAssert.Contains(repository, "FROM dbo.SupplierProgressReport");
        StringAssert.Contains(repository, "receiveHead.DocumentType=@PurchaseReceipt");
        StringAssert.Contains(repository, "receiveHead.PostingStatus IN @FormalPostingStatuses");
        StringAssert.Contains(repository, "SUM(receiveLine.Quantity) AS ReceivedQty");
        StringAssert.Contains(repository, "GROUP BY receiveLine.SourceLineId");
        StringAssert.Contains(repository, "LEFT JOIN ReceiptFacts receipt ON receipt.SourceLineId=l.Uid");
        StringAssert.Contains(repository, "存在无法定位采购行的正式采购入库历史");
        Assert.IsFalse(repository.Contains("receiveLine.SourceType", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("receiveLine.SourceUid", StringComparison.Ordinal));
        StringAssert.Contains(repository, "product.MainImage");
        StringAssert.Contains(repository, "MAX(f.ExpectedReadyDate) AS ExpectedCompletionDate");
        StringAssert.Contains(repository, "l.MaterialType=@CustomerSuppliedMaterialType");
        Assert.IsFalse(repository.Contains("CustomerNameSnapshot AS CustomerName", StringComparison.Ordinal));
    }

    /// <summary>确认管理查询不携带完整跟进历史、追溯或库存流水明细。</summary>
    [TestMethod]
    public void ManagerQuery_MustRemainNarrowAndIsolatedFromLegacyWorkspace()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrderManagerQueryRepository.cs");
        var service = Read("SwiftExport.Application", "Services", "Procurement", "OrderManagement", "PurchaseOrderManagerQueryService.cs");

        Assert.IsFalse(repository.Contains("PurchaseOrderWorkspace", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("dbo.DemandExecutionTrace", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("dbo.InventoryLedger", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("dbo.BusinessOperationLog", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("IPurchaseOrderWorkspace", StringComparison.Ordinal));
    }

    /// <summary>从仓库根目录读取源码契约，避免测试依赖构建输出目录结构。</summary>
    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    /// <summary>向上查找包含解决方案文件的仓库根目录。</summary>
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SwiftExport-WebAPI.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("未找到 SwiftExport 仓库根目录。");
    }
}
