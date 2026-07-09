using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SalesOrders.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 销售订单行数据源仓储接口。
/// 负责 CustomerProduct + Product 联查分页。
/// </summary>
public interface ISalesOrderLineRepository : IRepository<SalesOrderLine>
{
    /// <summary>
    /// 销售产品分页查询。
    /// 直接接收 SalesProductSearchRequest，由仓储层按业务规则解析：
    /// 1. CustomerId/CustomerCode 决定是否联 CustomerProduct；
    /// 2. BatchCustomerSkuList 做客户SKU精确匹配；
    /// 3. BatchKeywords 做 SKU/描述/特性/客户SKU 模糊匹配。
    /// </summary>
    Task<PagedResponseBase<SalesProductDto>> GetSalesProductPagedListAsync(SalesProductSearchRequest request, string orderBy = "cp.CreatedDate DESC");
    Task<IReadOnlyList<SalesOrderLine>> GetLinesByOrderIdAsync(Guid orderId, bool withUpdLock = false);
    Task<IReadOnlyList<SalesOrderLineManagerDto>> GetSalesOrderLineManagerAsync(Guid orderId);
}
