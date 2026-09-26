using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace SwiftExport.Contracts.Apis.SystemApis
{

    [Headers("Content-Type: application/json")]
    public interface ISysDicCurrencyExchangeApi : IGenericApi<SysDicCurrencyExchangeDto>
    {
        [Get("/all")]
        Task<ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>> GetAllAsync();

        [Post("/page")]
        Task<ServiceResult<PagedResponseBase<SysDicCurrencyExchangeDto>>> GetPagedAsync([Body] SysDicCurrencyExchangeSearchRequest request);

        [Post("/batch-save")]
        Task<ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>> BatchSaveAsync([Body] SysDicCurrencyExchangeBatchSaveRequest request);

    }
}
