using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.ExchangeRates;
namespace SwiftExport.Application.Interfaces.Systems.ExchangeRates;

/// <summary>
/// 货币汇率服务接口。
/// </summary>
public partial interface ISysDicCurrencyExchangeService : IBaseService<SysDicCurrencyExchange, SysDicCurrencyExchangeDto>
{

    Task<ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>> GetAllAsync();
    Task<ServiceResult<PagedResponseBase<SysDicCurrencyExchangeDto>>> GetPagedAsync(SysDicCurrencyExchangeSearchRequest request);
    Task<ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>> BatchSaveAsync(SysDicCurrencyExchangeBatchSaveRequest batchReq);

}
