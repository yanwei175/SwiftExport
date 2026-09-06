using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Orders;

/// <summary>读取销售履约 Manager 的订单级分页投影。</summary>
public interface ISalesFulfillmentManagerRepository
{
    /// <summary>按销售筛选条件返回订单级履约摘要，不写入任何业务状态。</summary>
    Task<PagedResponseBase<SalesFulfillmentManagerListItemDto>> SearchAsync(
        SalesFulfillmentManagerSearchRequest request,
        CancellationToken cancellationToken = default);
}
