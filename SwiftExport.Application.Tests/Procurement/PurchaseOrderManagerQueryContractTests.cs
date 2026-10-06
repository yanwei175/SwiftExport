using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>冻结采购管理新链查询的路由、聚合来源与旧链隔离边界。</summary>
[TestClass]
public sealed class PurchaseOrderManagerQueryContractTests
{
    /// <summary>确认管理接口只声明查询路由，快速命令仍复用既有订单控制器。</summary>
    [TestMethod]
    public void Controller_MustExposeManagerQueriesWithoutDuplicatingCommands()
    {
        var manager = Read("SwiftExport.WebApi", "Controllers", "Procurement", "PurchaseOrders", "Query", "PurchaseOrderManagerQueryController.cs");
        var orders = Read("SwiftExport.WebApi", "Controllers", "Procurement", "PurchaseOrders", "Confirmation", "ProcurementOrderConfirmationController.cs")
            + Read("SwiftExport.WebApi", "Controllers", "Procurement", "PurchaseOrders", "Reversal", "ProcurementOrderReversalController.cs");

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
        var source = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrders", "Query", "PostgreSqlPurchaseOrderManagerQueryRepository.cs");
        var repository = Sql("SearchSql") + Sql("LinesSql");
        Assert.IsFalse(repository.Contains("ExecutionModel", StringComparison.Ordinal));
        StringAssert.Contains(repository, "h.confirmation_status=ANY(@ConfirmationStatuses)");
        StringAssert.Contains(repository, "h.lifecycle_status=ANY(@LifecycleStatuses)");
        StringAssert.Contains(repository, "p.customer_code AS \"CustomerCode\"");
        StringAssert.Contains(repository, "OR h.customer_code COLLATE public.erp_dictionary_ci LIKE @Keyword");
        StringAssert.Contains(repository, "GROUP BY h.uid,h.version,h.lifecycle_status,h.order_no,h.bo_no,h.customer_code");
        StringAssert.Contains(repository, "LEFT JOIN LATERAL (SELECT");
        StringAssert.Contains(repository, "LIMIT 1");
        StringAssert.Contains(repository, "FROM public.supplier_progress_report");
        StringAssert.Contains(repository, "rh.document_type=@PurchaseReceipt");
        StringAssert.Contains(repository, "rh.posting_status IN(1,8)");
        StringAssert.Contains(repository, "SUM(CASE WHEN rh.posting_status=1 THEN rl.quantity ELSE 0 END) AS received_qty");
        StringAssert.Contains(repository, "GROUP BY rl.source_line_id");
        StringAssert.Contains(repository, "LEFT JOIN receipt_facts receipt ON receipt.source_line_id=l.uid");
        StringAssert.Contains(source, "EnsureFormalReceiptHistoryIsTraceableAsync(cancellationToken)");
        StringAssert.Contains(source, "存在无法定位采购行的正式采购入库历史");
        Assert.IsFalse(repository.Contains("rl.source_type", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("rl.source_uid", StringComparison.Ordinal));
        StringAssert.Contains(repository, "product.main_image");
        StringAssert.Contains(repository, "MAX(f.expected_ready_date) AS expected_completion_date");
        StringAssert.Contains(repository, "rtrim(l.material_type,' ') COLLATE public.erp_dictionary_ci=@CustomerSuppliedMaterialType");
        Assert.IsFalse(repository.Contains("customer_name_snapshot AS \"CustomerName\"", StringComparison.Ordinal));
    }

    /// <summary>确认管理查询不携带完整跟进历史、追溯或库存流水明细。</summary>
    [TestMethod]
    public void ManagerQuery_MustRemainNarrowAndIsolatedFromLegacyWorkspace()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrders", "Query", "PostgreSqlPurchaseOrderManagerQueryRepository.cs");
        var service = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrders", "Query", "PurchaseOrderManagerQueryService.cs");

        Assert.IsFalse(repository.Contains("PurchaseOrderWorkspace", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("public.demand_execution_trace", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("public.inventory_ledger_entry", StringComparison.Ordinal));
        Assert.IsFalse(repository.Contains("public.business_operation_log", StringComparison.Ordinal));
        Assert.IsFalse(service.Contains("IPurchaseOrderWorkspace", StringComparison.Ordinal));
    }

    /// <summary>读取当前 PG 实际执行的静态 SQL，不连接数据库。</summary>
    private static string Sql(string name)
        => (string)typeof(PostgreSqlPurchaseOrderManagerQueryRepository).GetField(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;

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
