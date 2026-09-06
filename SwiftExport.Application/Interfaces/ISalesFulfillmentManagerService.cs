using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces;

/// <summary>销售履约 Manager 的订单级只读查询用例。</summary>
public interface ISalesFulfillmentManagerService
{
    /// <summary>校验分页筛选并返回销售订单履约摘要。</summary>
    Task<ServiceResult<PagedResponseBase<SalesFulfillmentManagerListItemDto>>> SearchAsync(
        SalesFulfillmentManagerSearchRequest request,
        CancellationToken cancellationToken = default);
}
