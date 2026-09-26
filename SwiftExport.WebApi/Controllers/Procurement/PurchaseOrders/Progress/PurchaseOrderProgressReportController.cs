using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Procurement.PurchaseOrders.Progress;

/// <summary>采购Progress用例的 HTTP 边界；保留既有路由和授权属性。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/purchase-order-progress")]
public sealed class PurchaseOrderProgressReportController(IPurchaseOrderProgressReportService reports) : ControllerBase
{
    /// <summary>追加供应商累计反馈；防重复窗口仅作为传输层补充，业务幂等由 CommandId 保证。</summary>
    [HttpPost("{purchaseOrderId:guid}/reports")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<PurchaseOrderProgressReportResult>> ReportAsync(
        Guid purchaseOrderId,
        [FromBody] ReportPurchaseOrderProgressCommand command,
        CancellationToken cancellationToken)
        => reports.ReportAsync(purchaseOrderId, command, cancellationToken);
}
