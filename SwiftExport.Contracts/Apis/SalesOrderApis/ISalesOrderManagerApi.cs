using Refit;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SalesOrderApis
{
    /// <summary>
    /// 销售订单 Manager 的列表查询契约。
    /// </summary>
    public interface ISalesOrderManagerApi
    {
        /// <summary>按管理页条件分页查询销售订单摘要。</summary>
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>> SearchAsync(
            [Body] SalesOrderManagerSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>获取管理页客户代码筛选建议。</summary>
        [Get("/customer-codes")]
        Task<ServiceResult<List<string>>> GetCustomerCodesAsync(
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
