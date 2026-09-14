using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Procurement;

/// <summary>采购跟进页专用只读端口；不提供采购状态或库存写入。</summary>
public interface IPurchaseOrderProgressQueryRepository
{
    /// <summary>分页读取采购头的跟进摘要。</summary>
    Task<PagedResponseBase<PurchaseOrderProgressListItemDto>> SearchAsync(
        PurchaseOrderProgressSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>读取采购行及完整供应商反馈历史。</summary>
    Task<PurchaseOrderProgressDetailDto?> GetDetailAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default);
}
