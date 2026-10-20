using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Procurement;

/// <summary>供应商反馈录入专用只读端口；不提供采购状态或库存写入。</summary>
public interface IPurchaseOrderProgressQueryRepository
{
    /// <summary>分页读取可识别订单、有效行数和登记资格，不汇总反馈进度。</summary>
    Task<PagedResponseBase<PurchaseOrderProgressListItemDto>> SearchAsync(
        PurchaseOrderProgressSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>按采购单身份读取当前头版本、登记资格、采购行及完整反馈历史。</summary>
    Task<PurchaseOrderProgressDetailDto?> GetDetailAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default);
}
