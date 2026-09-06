using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;

namespace SwiftExport.Core.Interfaces.Orders;

/// <summary>读取单张销售订单的履约、采购组件和订单级采购关联。</summary>
public interface ISalesFulfillmentDetailRepository
{
    /// <summary>返回销售履约 Detail；不存在时返回 null。</summary>
    Task<SalesFulfillmentDetailDto?> GetAsync(Guid salesOrderId, CancellationToken cancellationToken = default);
}
