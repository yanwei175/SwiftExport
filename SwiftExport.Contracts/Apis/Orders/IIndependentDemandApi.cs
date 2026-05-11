using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Orders
{
    /// <summary>
    /// 销售毛需求 API。
    /// </summary>
    public interface IIndependentDemandApi : IGenericApi<IndependentDemandDto>
    {
        /// <summary>
        /// 按来源单据触发销售毛需求拆解。
        /// </summary>
        [Post("/decompose-by-source")]
        Task<ServiceResult<List<DTOs.Orders.IndependentDemandDto>>> ExecuteDecomposeBySourceAsync([Body] IndependentDemandDecomposeRequestDto request);
    }
}
