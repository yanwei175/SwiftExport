using Refit;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.Wrappers;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SalesOrderApis
{
    /// <summary>销售履约 Manager 的只读 API。</summary>
    public interface ISalesFulfillmentManagerApi
    {
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<SalesFulfillmentManagerListItemDto>>> SearchAsync(
            [Body] SalesFulfillmentManagerSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
