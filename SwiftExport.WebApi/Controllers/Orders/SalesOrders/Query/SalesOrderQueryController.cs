using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Orders.SalesOrders.Query;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Orders.SalesOrders.Query;

/// <summary>按原 HTTP 路由提供销售列表、详情与候选查询。</summary>
[ApiController]
public sealed class SalesOrderQueryController(ISalesOrderQueryService service) : ControllerBase
{
    /// <summary>按管理页条件分页查询销售订单。</summary>
    [HttpPost("api/sales-orders/manager/search")]
    public Task<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>> SearchAsync([FromBody] SalesOrderManagerSearchRequest request, CancellationToken cancellationToken)
        => service.SearchAsync(request, cancellationToken);

    /// <summary>查询客户代码建议。</summary>
    [HttpGet("api/sales-orders/manager/customer-codes")]
    public Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken)
        => service.GetCustomerCodesAsync(cancellationToken);

    /// <summary>读取完整销售订单。</summary>
    [HttpGet("api/sales-orders/detail/{uid:guid}")]
    public Task<ServiceResult<SalesOrderDetailDto>> GetAsync([FromRoute] Guid uid, CancellationToken cancellationToken)
        => service.GetAsync(uid, cancellationToken);

    /// <summary>查询产品候选。</summary>
    [HttpPost("api/sales-orders/detail/products/search")]
    public Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync([FromBody] SalesOrderProductSearchRequest request, CancellationToken cancellationToken)
        => service.SearchProductsAsync(request, cancellationToken);

    /// <summary>查询客户候选。</summary>
    [HttpPost("api/sales-orders/detail/customers/search")]
    public Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync([FromBody] SalesOrderCustomerSearchRequest request, CancellationToken cancellationToken)
        => service.SearchCustomersAsync(request, cancellationToken);
}
