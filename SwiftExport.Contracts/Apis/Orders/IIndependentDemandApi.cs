using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.Independent;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Orders
{
    /// <summary>
    /// 销售毛需求 API。
    /// </summary>
    public interface IIndependentDemandApi 
    {
        [Get("/page")]
        Task<ServiceResult<PagedResultDto<IndependentDemandDto>>> GetPagedAsync([Body] IndependSearchRequest request);

        [Post("/batch-save")]
        Task<ServiceResult<IndependentBatchSaveResponse>> BatchSaveAsync([Body] IndependentBatchSaveRequest request);

        /// <summary>
        /// 按来源单据触发销售毛需求拆解。
        /// </summary>
        [Post("/decompose-by-source")]
        Task<ServiceResult<List<IndependentDemandDto>>> ExecuteDecomposeBySourceAsync([Body] IndependentDemandDecomposeRequestDto request);






    }
}
