using SwiftExport.Core.Entities.Systems.ExchangeRates;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Systems.ExchangeRates
{
    /// <summary>
    /// SysDicCurrencyExchange 仓储接口
    /// </summary>
    public interface ISysDicCurrencyExchangeRepository : IRepository<SysDicCurrencyExchange>
    {
        Task<PagedResponseBase<SysDicCurrencyExchangeDto>> GetPagedAsync(SysDicCurrencyExchangeSearchRequest request);

        Task<IReadOnlyList<SysDicCurrencyExchange>> GetAllForUpdateAsync();

        Task<IReadOnlyList<string>> GetReferencedCurrencyCodesAsync(IEnumerable<string> currencyCodes);
    }
}
