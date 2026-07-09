using Refit;
using SwiftExport.Contracts.DTOs.Independent;
using SwiftExport.Contracts.DTOs.Independent.Requests;
using SwiftExport.Contracts.DTOs.Independent.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Orders
{
    /// <summary>
    /// 毛需求 API。
    /// </summary>
    public interface IIndependentDemandApi
    {
        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<IndependentDemandDto>>> GetPagedAsync([Body] IndependSearchRequest request);

        [Post("/batch-save")]
        Task<ServiceResult<IndependentBatchSaveResponse>> BatchSaveAsync([Body] IndependentBatchSaveRequest request);

        /// <summary>
        /// 按来源单据触发销售毛需求拆解。
        /// </summary>
        [Post("/decompose-by-source")]
        Task<ServiceResult<List<IndependentDemandDto>>> ExecuteDecomposeBySourceAsync([Body] IndependentDemandDecomposeRequestDto request);


    }
}
