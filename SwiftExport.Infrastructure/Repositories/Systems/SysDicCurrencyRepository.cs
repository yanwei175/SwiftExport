using Dapper;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems
{
    /// <summary>
    /// 货币主数据仓储实现
    /// 继承泛型 DapperRepository，扩展货币专属查询方法
    /// </summary>
    public partial class SysDicCurrencyRepository(RepositoryBaseContext context)
        : DapperRepository<SysDicCurrency>(context), ISysDicCurrencyRepository
    {
        public async Task<bool> ResetAllBaseCurrencyAsync()
        {
            var sql = $"UPDATE [{TableName}] SET RateToBase=null, IsBase = 0";
            await Connection.ExecuteAsync(sql, transaction: Transaction);
            return true;
        }

        /// <summary>
        /// 查询数据库中已设置为本位币的记录 ID 列表
        /// excludeIds：排除指定 ID（更新场景：排除本次要更新的行自身，避免误报）
        /// </summary>
        public async Task<IReadOnlyList<Guid>> GetBaseIsBaseCurrencyIdsAsync(IEnumerable<Guid>? excludeIds = null)
        {
            var excludeList = excludeIds?.ToList() ?? [];

            // 如果有排除 ID，则加上 NOT IN 条件
            var sql = excludeList.Count > 0
                ? $"SELECT Uid FROM [{TableName}] WHERE IsBase = 1 AND IsDeleted = 0 AND Uid NOT IN @ExcludeIds"
                : $"SELECT Uid FROM [{TableName}] WHERE IsBase = 1 AND IsDeleted = 0";

            var param = excludeList.Count > 0
                ? (object)new { ExcludeIds = excludeList }
                : new { };

            var result = await Connection.QueryAsync<Guid>(sql, param, transaction: Transaction);
            return result.ToList();
        }

        /// <summary>
        /// 获取数据库中已存在的货币代码列表（批量去重用）
        /// </summary>
        public async Task<IReadOnlyList<string>> GetExistingCurrencyCodesAsync(IEnumerable<string> currencyCodes)
        {
            var sql = $"SELECT CurrencyCode FROM [{TableName}] WHERE CurrencyCode IN @Codes AND IsDeleted = 0";
            var result = await Connection.QueryAsync<string>(sql, new { Codes = currencyCodes }, transaction: Transaction);
            return result.ToList();
        }

    }
}
