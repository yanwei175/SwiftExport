using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

/// <summary>采购供应商跟进只读应用边界；查询不改变采购确认或库存状态。</summary>
public interface IPurchaseOrderProgressQueryService
{
    /// <summary>查询反馈录入所需的订单选择信息和登记资格。</summary>
    Task<ServiceResult<PagedResponseBase<PurchaseOrderProgressListItemDto>>> SearchAsync(
        PurchaseOrderProgressSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>按采购单身份读取当前头版本、登记资格、采购行及完整反馈历史。</summary>
    Task<ServiceResult<PurchaseOrderProgressDetailDto>> GetDetailAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default);

}
