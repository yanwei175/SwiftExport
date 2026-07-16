using Microsoft.AspNetCore.Mvc;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Orders;

/// <summary>
/// 新销售订单工作区 HTTP 边界。本阶段仅建立可编译契约，禁止转调旧状态服务。
/// </summary>
[ApiController]
[Route("api/sales-orders/workspace")]
public sealed class SalesOrderWorkspaceController : ControllerBase
{
    private const string NotImplementedMessage =
        "销售订单工作区后端尚未实现。请按 _codex_handoff/销售订单工作区后端与数据库交接.md 完成 Application、Repository 和事务逻辑。";

    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<SalesOrderListItemDto>>> SearchAsync(
        [FromBody] SalesOrderSearchRequest request,
        CancellationToken cancellationToken)
        => Pending<PagedResponseBase<SalesOrderListItemDto>>();

    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<SalesOrderDocumentDto>> GetDocumentAsync(
        [FromRoute] Guid uid,
        CancellationToken cancellationToken)
        => Pending<SalesOrderDocumentDto>();

    [HttpPost("products/search")]
    public Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync(
        [FromBody] SalesOrderProductSearchRequest request,
        CancellationToken cancellationToken)
        => Pending<PagedResponseBase<SalesOrderProductLookupDto>>();

    [HttpPost("customers/search")]
    public Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync(
        [FromBody] SalesOrderCustomerSearchRequest request,
        CancellationToken cancellationToken)
        => Pending<PagedResponseBase<SalesOrderCustomerLookupDto>>();

    [HttpPost("save")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SaveSalesOrderResponse>> SaveAsync([FromBody] SaveSalesOrderRequest request)
        => Pending<SaveSalesOrderResponse>();

    [HttpPost("{uid:guid}/confirm")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SalesOrderConfirmationResultDto>> ConfirmAsync(
        [FromRoute] Guid uid,
        [FromBody] SalesOrderCommandRequest request)
        => Pending<SalesOrderConfirmationResultDto>();

    [HttpPost("{uid:guid}/cancel-confirmation")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync(
        [FromRoute] Guid uid,
        [FromBody] SalesOrderCommandRequest request)
        => Pending<SalesOrderConfirmationResultDto>();

    [HttpPost("{uid:guid}/close")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<SalesOrderDocumentDto>> CloseAsync(
        [FromRoute] Guid uid,
        [FromBody] CloseSalesOrderRequest request)
        => Pending<SalesOrderDocumentDto>();

    [HttpPost("{uid:guid}/delete-draft")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<bool>> DeleteDraftAsync(
        [FromRoute] Guid uid,
        [FromBody] SalesOrderCommandRequest request)
        => Pending<bool>();

    private static Task<ServiceResult<T>> Pending<T>()
        => Task.FromResult(ServiceResult<T>.Fail(NotImplementedMessage));
}
