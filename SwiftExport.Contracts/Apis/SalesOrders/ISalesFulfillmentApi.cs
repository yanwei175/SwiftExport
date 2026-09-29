using Refit;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SalesOrderApis
{
    /// <summary>按销售订单读取只读履约证据。</summary>
    public interface ISalesFulfillmentApi
    {
        /// <summary>一次读取指定订单的行级需求、采购、货好与正式交付。</summary>
        [Get("/{uid}")]
        Task<ServiceResult<SalesFulfillmentDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
    }
}
