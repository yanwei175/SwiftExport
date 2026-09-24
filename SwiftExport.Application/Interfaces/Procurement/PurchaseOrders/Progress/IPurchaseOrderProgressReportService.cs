using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

/// <summary>采购供应商反馈追加应用边界；写入历史而不改变采购确认或库存状态。</summary>
public interface IPurchaseOrderProgressReportService
{
    /// <summary>追加供应商累计反馈；同一命令标识重放返回原报告。</summary>
    Task<ServiceResult<PurchaseOrderProgressReportResult>> ReportAsync(
        Guid purchaseOrderId,
        ReportPurchaseOrderProgressCommand command,
        CancellationToken cancellationToken = default);
}
