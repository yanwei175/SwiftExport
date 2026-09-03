using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Core.Interfaces.Procurement;
/// <summary>仅查询新采购管理所需投影，不组装旧 Workspace 详情。</summary>
public interface IProcurementOrderQueryRepository
{
    /// <summary>按稳定排序取得新链管理分页。</summary>
    Task<PagedResponseBase<ProcurementOrderListItemDto>> SearchAsync(ProcurementOrderSearchRequest request, CancellationToken ct);
    /// <summary>读取订单自身和采购行历史快照。</summary>
    Task<PurchaseOrderDetailDto?> GetDetailAsync(Guid uid, CancellationToken ct);
}
