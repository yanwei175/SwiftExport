using SwiftExport.Core.Entities.Systems.Currencies;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Systems.Currencies
{
    /// <summary>
    /// SysDicCurrency 仓储接口
    /// 扩展了本位币查询等特定业务方法
    /// </summary>
    public interface ISysDicCurrencyRepository : IRepository<SysDicCurrency>
    {

        Task<bool> ResetAllBaseCurrencyAsync();
        /// <summary>
        /// 查询当前数据库中已设为本位币的记录 ID 列表
        /// 用于校验"本位币唯一性"约束
        /// </summary>
        /// <param name="excludeIds">需要排除的 ID（通常是正在更新的行自身）</param>
        Task<IReadOnlyList<Guid>> GetBaseIsBaseCurrencyIdsAsync(IEnumerable<Guid>? excludeIds = null);

        /// <summary>
        /// 校验货币代码是否已存在（用于新增去重）
        /// </summary>
        Task<IReadOnlyList<string>> GetExistingCurrencyCodesAsync(IEnumerable<string> currencyCodes);

        Task<PagedResponseBase<SysDicCurrencyDto>> GetPagedAsync(SysDicCurrencySearchRequest request);

        Task<IReadOnlyList<SysDicCurrency>> GetAllForUpdateAsync();

        Task<IReadOnlyList<SysDicCurrency>> GetByCurrencyCodesAsync(IEnumerable<string> currencyCodes, bool withUpdLock = false);

        Task<IReadOnlyList<SysDicCurrency>> GetBaseCurrenciesAsync();

        Task<IReadOnlyList<SysDicCurrency>> GetEnabledCurrenciesAsync();

        Task<IReadOnlyList<string>> GetBusinessReferencedCurrencyCodesAsync(IEnumerable<string> currencyCodes);
    }
}
