using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.SystemDtos;
namespace SwiftExport.Contracts.Apis.SystemApis
{

    [Headers("Content-Type: application/json")]
    public interface ISysDicCurrencyExchangeApi : IGenericApi<SysDicCurrencyExchangeDto>
    {

    }
}
