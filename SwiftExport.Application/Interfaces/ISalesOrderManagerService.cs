using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces;

/// <summary>
/// 定义销售订单 Manager 的列表查询能力。
/// </summary>
public interface ISalesOrderManagerService
{
    /// <summary>按管理页条件分页查询销售订单摘要。</summary>
    Task<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>> SearchAsync(
        SalesOrderManagerSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>获取管理页客户代码筛选建议。</summary>
    Task<ServiceResult<List<string>>> GetCustomerCodesAsync(
        CancellationToken cancellationToken = default);
}
