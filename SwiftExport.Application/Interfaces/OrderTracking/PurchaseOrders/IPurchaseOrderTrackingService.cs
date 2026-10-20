using SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.OrderTracking.PurchaseOrders;

/// <summary>采购单跟踪的只读用例，按供应商反馈与正式收货维度分别授权。</summary>
public interface IPurchaseOrderTrackingService
{
    /// <summary>按采购单分页，无需求关联的独立采购同样可见。</summary>
    Task<ServiceResult<PagedResponseBase<PurchaseOrderTrackingItemDto>>> SearchPurchaseOrdersAsync(PurchaseOrderTrackingSearchRequest request,CancellationToken cancellationToken=default);
    /// <summary>读取指定采购单的全部有效行，不继承搜索筛选。</summary>
    Task<ServiceResult<List<PurchaseOrderTrackingLineDto>>> GetPurchaseOrderLinesAsync(Guid purchaseOrderId,CancellationToken cancellationToken=default);
    /// <summary>读取采购整行收货进度及完整正式证据。</summary>
    Task<ServiceResult<PurchaseOrderTrackingReceiptsDto>> GetPurchaseOrderReceiptsAsync(Guid purchaseOrderId,CancellationToken cancellationToken=default);
    /// <summary>读取指定采购单完整供应商反馈历史。</summary>
    Task<ServiceResult<List<SupplierProgressReportDto>>> GetPurchaseOrderReportsAsync(Guid purchaseOrderId,CancellationToken cancellationToken=default);
}
