using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Orders.SalesOrders.Confirmation;
using Microsoft.AspNetCore.Authorization;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Orders;

/// <summary>
/// 销售订单 Detail 的读取、编辑及生命周期命令 HTTP 边界。
/// </summary>
[ApiController]
[Route("api/sales-orders/detail")]
public sealed class SalesOrderDetailController(ISalesOrderDetailService service, ISalesOrderConfirmationService confirmations) : ControllerBase
{
    /// <summary>返回指定销售订单的完整详情。</summary>
    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<SalesOrderDetailDto>> GetAsync(
        [FromRoute] Guid uid,
        CancellationToken cancellationToken)
        => service.GetAsync(uid, cancellationToken);

    /// <summary>查询销售订单行可选择的产品。</summary>
    [HttpPost("products/search")]
    public Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync(
        [FromBody] SalesOrderProductSearchRequest request,
        CancellationToken cancellationToken)
        => service.SearchProductsAsync(request, cancellationToken);

    /// <summary>查询销售订单可选择的客户。</summary>
    [HttpPost("customers/search")]
    public Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync(
        [FromBody] SalesOrderCustomerSearchRequest request,
        CancellationToken cancellationToken)
        => service.SearchCustomersAsync(request, cancellationToken);

    /// <summary>保存销售订单草稿。</summary>
    [HttpPost("save")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SaveSalesOrderResponse>> SaveAsync([FromBody] SaveSalesOrderRequest request)
        => service.SaveAsync(request);

    /// <summary>确认销售订单并接收来源需求。</summary>
    [HttpPost("{uid:guid}/confirm")]
    [Authorize]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SalesOrderConfirmationResultDto>> ConfirmAsync(
        [FromRoute] Guid uid,
        [FromBody] ConfirmSalesOrderCommand request)
        => confirmations.ConfirmAsync(uid, request);

    /// <summary>在下游历史安全时取消销售订单确认。</summary>
    [HttpPost("{uid:guid}/cancel-confirmation")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync(
        [FromRoute] Guid uid,
        [FromBody] CancelSalesOrderConfirmationCommand request)
        => service.CancelConfirmationAsync(uid, request);

    /// <summary>关闭销售订单。</summary>
    [HttpPost("{uid:guid}/close")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SalesOrderDetailDto>> CloseAsync(
        [FromRoute] Guid uid,
        [FromBody] CloseSalesOrderRequest request)
        => service.CloseAsync(uid, request);

    /// <summary>删除未确认且无来源需求的销售订单草稿。</summary>
    [HttpPost("{uid:guid}/delete-draft")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<bool>> DeleteDraftAsync(
        [FromRoute] Guid uid,
        [FromBody] SalesOrderCommandRequest request)
        => service.DeleteDraftAsync(uid, request);
}
