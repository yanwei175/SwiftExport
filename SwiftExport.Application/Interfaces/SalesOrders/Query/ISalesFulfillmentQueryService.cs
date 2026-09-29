using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.SalesOrders.Query;

/// <summary>销售订单只读履约证据查询入口。</summary>
public interface ISalesFulfillmentQueryService
{
    /// <summary>按销售订单身份读取当下有效的履约事实。</summary>
    Task<ServiceResult<SalesFulfillmentDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default);
}
