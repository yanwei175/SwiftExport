using Refit;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SalesOrderApis
{
    /// <summary>销售履约 Detail 的只读 API。</summary>
    public interface ISalesFulfillmentDetailApi
    {
        [Get("/{salesOrderId}")]
        Task<ServiceResult<SalesFulfillmentDetailDto>> GetAsync(Guid salesOrderId, CancellationToken cancellationToken = default(CancellationToken));
    }
}
