using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.SalesOrders.Draft;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.SalesOrders.Draft;

/// <summary>按原路由转发销售草稿保存与删除。</summary>
[ApiController, Route("api/sales-orders/detail")]
public sealed class SalesOrderDraftController(ISalesOrderDraftService service) : ControllerBase
{
    /// <summary>保存销售订单草稿。</summary>
    [HttpPost("save"), PreventDuplicateRequest]
    public Task<ServiceResult<SaveSalesOrderResponse>> SaveAsync([FromBody] SaveSalesOrderRequest request) => service.SaveAsync(request);

    /// <summary>删除未确认且无来源需求的草稿。</summary>
    [HttpPost("{uid:guid}/delete-draft"), PreventDuplicateRequest]
    public Task<ServiceResult<bool>> DeleteDraftAsync([FromRoute] Guid uid, [FromBody] SalesOrderCommandRequest request) => service.DeleteDraftAsync(uid, request);
}
