using SwiftExport.Core.Entities.Systems;

namespace SwiftExport.Core.Interfaces
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
    }
}
