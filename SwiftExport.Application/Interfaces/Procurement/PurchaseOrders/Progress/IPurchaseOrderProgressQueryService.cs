using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

/// <summary>采购供应商跟进只读应用边界；查询不改变采购确认或库存状态。</summary>
public interface IPurchaseOrderProgressQueryService
{
    /// <summary>查询跟进工作台采购头摘要。</summary>
    Task<ServiceResult<PagedResponseBase<PurchaseOrderProgressListItemDto>>> SearchAsync(
        PurchaseOrderProgressSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>读取采购行及完整供应商反馈历史。</summary>
    Task<ServiceResult<PurchaseOrderProgressDetailDto>> GetDetailAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default);

}
