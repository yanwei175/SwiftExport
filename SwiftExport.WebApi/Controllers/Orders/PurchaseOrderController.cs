using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders.Requests;
using SwiftExport.Contracts.DTOs.Orders.Responses;
using SwiftExport.Contracts.DTOs.PurchaseOrders;
using SwiftExport.Contracts.DTOs.PurchaseOrders.Requests;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.Filters;
namespace SwiftExport.WebApi.Controllers.Orders;


public class PurchaseOrderController(IPurchaseOrderService service) : BaseController<PurchaseOrderHead, PurchaseOrderHeadDto>(service)
{

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<PurchaseOrderBatchSaveResponse>> BatchSave([FromBody] PurchaseOrderBatchSaveRequest req) => await service.BatchSaveAsync(req);

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<PurchaseOrderHeadDto>>> GetPagedListAsync([FromBody] PurchaseOrderSearchRequest request) => await service.GetPagedAsync(request);


    [HttpGet("{id}/detail")]
    public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByIdAsync(Guid id)
    {
        return await service.GetDetailByHeadIdAsync(id);
    }

    [HttpGet("by-orderno/{orderNo}")]
    public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByOrderNoAsync(string orderNo)
    {
        return await service.GetDetailByOrderNoAsync(orderNo);
    }

    [HttpGet("by-pono/{poNo}")]
    public async Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailByPoNoAsync(string poNo)
    {
        return await service.GetDetailByBoNoAsync(poNo);
    }
    [HttpGet("awaiting-inbound-suppliers")]
    public async Task<ServiceResult<List<SupplierDto>>> GetAwatingInboudSuppliersAsync()
    {
        return await service.GetAwaitingInboundSuppliersAsync();
    }

    [HttpPost("detail/save")]
    public async Task<ServiceResult<Guid>> SaveDetailAsync([FromBody] PurchaseOrderDetailDto request)
    {
        return await service.SaveDetailAsync(request);
    }
}
