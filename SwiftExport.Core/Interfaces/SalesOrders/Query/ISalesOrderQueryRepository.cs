using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Core.Interfaces.SalesOrders.Query;

/// <summary>销售订单列表与详情的只读仓储契约。</summary>
public interface ISalesOrderQueryRepository
{
    /// <summary>按管理页条件读取稳定分页列表。</summary>
    Task<PagedResponseBase<SalesOrderManagerListItemDto>> SearchManagerAsync(SalesOrderManagerSearchRequest request, DateTime businessToday, CancellationToken cancellationToken = default);
    /// <summary>读取订单头行及编辑投影。</summary>
    Task<SalesOrderDetailDto?> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>一次读取订单的行级履约证据，不返回写模型。</summary>
    Task<SalesFulfillmentDetailDto?> GetFulfillmentAsync(Guid uid, CancellationToken cancellationToken = default);
}
