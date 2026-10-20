using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.OrderTracking;

/// <summary>采购单视角的订单跟踪HTTP边界，细权限由过滤器与应用服务共同保护。</summary>
[Authorize]
[ApiController]
[Route("api/order-tracking/purchase-orders")]
public sealed class PurchaseOrderTrackingController(IPurchaseOrderTrackingService service):ControllerBase
{
    /// <summary>按采购单分页搜索，包含独立采购单。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<PurchaseOrderTrackingItemDto>>> SearchPurchaseOrdersAsync([FromBody]PurchaseOrderTrackingSearchRequest request,CancellationToken cancellationToken)
        =>service.SearchPurchaseOrdersAsync(request,cancellationToken);
    /// <summary>按当前采购单读取全部有效行。</summary>
    [HttpGet("{purchaseOrderId:guid}/lines")]
    public Task<ServiceResult<List<PurchaseOrderTrackingLineDto>>> GetPurchaseOrderLinesAsync([FromRoute]Guid purchaseOrderId,CancellationToken cancellationToken)
        =>service.GetPurchaseOrderLinesAsync(purchaseOrderId,cancellationToken);
    /// <summary>读取采购整行收货进度及正式证据。</summary>
    [HttpGet("{purchaseOrderId:guid}/receipts")]
    public Task<ServiceResult<PurchaseOrderTrackingReceiptsDto>> GetPurchaseOrderReceiptsAsync([FromRoute]Guid purchaseOrderId,CancellationToken cancellationToken)
        =>service.GetPurchaseOrderReceiptsAsync(purchaseOrderId,cancellationToken);
    /// <summary>读取采购单完整供应商反馈历史。</summary>
    [HttpGet("{purchaseOrderId:guid}/reports")]
    public Task<ServiceResult<List<SupplierProgressReportDto>>> GetPurchaseOrderReportsAsync([FromRoute]Guid purchaseOrderId,CancellationToken cancellationToken)
        =>service.GetPurchaseOrderReportsAsync(purchaseOrderId,cancellationToken);
}
