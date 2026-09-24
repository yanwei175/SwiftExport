using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;
/// <summary>新采购管理只读用例，订单详情和确认依据分别取得。</summary>
public interface IProcurementOrderQueryService
{
    /// <summary>取得新链采购摘要分页。</summary>
    Task<ServiceResult<PagedResponseBase<ProcurementOrderListItemDto>>> SearchAsync(ProcurementOrderSearchRequest request, CancellationToken ct = default);
    /// <summary>取得订单自身历史快照。</summary>
    Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailAsync(Guid uid, CancellationToken ct = default);
}
