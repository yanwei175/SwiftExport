using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces;

/// <summary>销售履约 Detail 的单据级只读查询用例。</summary>
public interface ISalesFulfillmentDetailService
{
    /// <summary>返回指定销售订单的履约详情；不存在时返回失败结果。</summary>
    Task<ServiceResult<SalesFulfillmentDetailDto>> GetAsync(
        Guid salesOrderId,
        CancellationToken cancellationToken = default);
}
