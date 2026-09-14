using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

/// <summary>采购供应商跟进应用边界；查询与追加反馈均不改变采购确认或库存状态。</summary>
public interface IPurchaseOrderProgressService
{
    /// <summary>查询跟进工作台采购头摘要。</summary>
    Task<ServiceResult<PagedResponseBase<PurchaseOrderProgressListItemDto>>> SearchAsync(
        PurchaseOrderProgressSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>读取采购行及完整供应商反馈历史。</summary>
    Task<ServiceResult<PurchaseOrderProgressDetailDto>> GetDetailAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>追加供应商累计反馈；同一命令标识重放返回原报告。</summary>
    Task<ServiceResult<PurchaseOrderProgressReportResult>> ReportAsync(
        Guid purchaseOrderId,
        ReportPurchaseOrderProgressCommand command,
        CancellationToken cancellationToken = default);
}
