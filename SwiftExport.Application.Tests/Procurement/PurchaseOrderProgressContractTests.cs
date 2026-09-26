using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>锁定采购跟进专用读写边界与追加式反馈规则。</summary>
[TestClass]
public sealed class PurchaseOrderProgressContractTests
{
    /// <summary>跟进 API 只暴露查询、详情和追加反馈三个专用端点。</summary>
    [TestMethod]
    public void Api_routes_are_narrow_and_cancellable()
    {
        var methods = typeof(IPurchaseOrderProgressApi).GetMethods().ToDictionary(method => method.Name);
        Assert.AreEqual("/search", Route<PostAttribute>(methods[nameof(IPurchaseOrderProgressApi.SearchAsync)]));
        Assert.AreEqual("/{purchaseOrderId}/detail", Route<GetAttribute>(methods[nameof(IPurchaseOrderProgressApi.GetDetailAsync)]));
        Assert.AreEqual("/{purchaseOrderId}/reports", Route<PostAttribute>(methods[nameof(IPurchaseOrderProgressApi.ReportAsync)]));
        Assert.IsTrue(methods.Values.All(method => method.GetParameters()
            .Any(parameter => parameter.ParameterType == typeof(CancellationToken))));
    }

    /// <summary>客户端不能伪造供应商报告时间，时间由服务端审计打戳。</summary>
    [TestMethod]
    public void Report_command_does_not_accept_client_reported_date()
    {
        Assert.IsNull(typeof(ReportPurchaseOrderProgressCommand).GetProperty("SupplierReportedDate"));
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrders", "Progress", "SupplierProgressReportRepository.cs");
        StringAssert.Contains(repository, "report.SupplierReportedDate = report.CreatedDate");
        StringAssert.Contains(repository, "context.AuditStamp.StampForInsert(report)");
    }

    /// <summary>反馈只追加，数量回退必须有纠错原因，并保留命令幂等。</summary>
    [TestMethod]
    public void Reporting_is_append_only_versioned_and_idempotent()
    {
        var service = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrders", "Progress", "PurchaseOrderProgressReportService.cs");
        foreach (var token in new[]
                 {
                     "BusinessCommandExecutor", "ExpectedPurchaseOrderVersion", "ExpectedLineVersion",
                     "command.ReadyQty < latest.ReadyQty", "累计已备好数量下降时必须填写纠错原因",
                     "Sequence = (latest?.Sequence ?? 0) + 1", "command.ReadyQty > line.Quantity"
                 })
            StringAssert.Contains(service, token);

        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrders", "Progress", "SupplierProgressReportRepository.cs");
        StringAssert.Contains(repository, "INSERT dbo.SupplierProgressReport");
        Assert.IsFalse(repository.Contains("UPDATE dbo.SupplierProgressReport", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(repository.Contains("DELETE FROM dbo.SupplierProgressReport", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(repository, "WITH(UPDLOCK,HOLDLOCK)");
    }

    /// <summary>跟进详情以采购行标识汇总正式收货，不依赖需求 Link。</summary>
    [TestMethod]
    public void Detail_uses_purchase_line_identity_for_receipts()
    {
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrders", "Progress", "PurchaseOrderProgressQueryRepository.cs");
        StringAssert.Contains(repository, "receipt.SourceLineId=l.Uid");
        StringAssert.Contains(repository, "receiveHead.PostingStatus IN @FormalPostingStatuses");
        Assert.IsFalse(repository.Contains("DemandExecutionLink", StringComparison.Ordinal));
    }

    /// <summary>供应商安排枚举数值已持久化，不得重排。</summary>
    [TestMethod]
    public void Supplier_arrangement_values_are_frozen()
        => CollectionAssert.AreEqual(
            new[] { 0, 1, 2, 8 },
            Enum.GetValues<SupplierArrangementStatusEnum>().Select(value => (int)value).ToArray());

    private static string Route<TAttribute>(System.Reflection.MethodInfo method)
        where TAttribute : HttpMethodAttribute
        => method.GetCustomAttributes(typeof(TAttribute), false).Cast<TAttribute>().Single().Path;

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。");
        return string.Empty;
    }
}
