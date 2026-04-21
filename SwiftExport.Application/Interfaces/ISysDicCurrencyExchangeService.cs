using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Core.Entities.Systems;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 货币汇率字典服务接口
    /// </summary>
    public interface ISysDicCurrencyExchangeService : IBaseService<SysDicCurrencyExchange, SysDicCurrencyExchangeDto>
    {
    }
}
