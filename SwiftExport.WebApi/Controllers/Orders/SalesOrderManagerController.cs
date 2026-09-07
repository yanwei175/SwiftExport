using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Orders;

/// <summary>
/// 销售订单 Manager 的列表查询 HTTP 边界。
/// </summary>
[ApiController]
[Route("api/sales-orders/manager")]
public sealed class SalesOrderManagerController(ISalesOrderManagerService service) : ControllerBase
{
    /// <summary>按管理页条件分页返回销售订单摘要。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>> SearchAsync(
        [FromBody] SalesOrderManagerSearchRequest request,
        CancellationToken cancellationToken)
        => service.SearchAsync(request, cancellationToken);

    /// <summary>返回管理页客户代码筛选建议。</summary>
    [HttpGet("customer-codes")]
    public Task<ServiceResult<List<string>>> GetCustomerCodesAsync(
        CancellationToken cancellationToken)
        => service.GetCustomerCodesAsync(cancellationToken);
}
