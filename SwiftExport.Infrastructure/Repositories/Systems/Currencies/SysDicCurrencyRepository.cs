using SwiftExport.Core.Interfaces.Systems.Currencies;
using Dapper;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Currencies;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems.Currencies
{
    /// <summary>
    /// 货币主数据仓储实现
    /// 继承泛型 DapperRepository，扩展货币专属查询方法
    /// </summary>
    public partial class SysDicCurrencyRepository(RepositoryBaseContext context)
        : DapperRepository<SysDicCurrency>(context), ISysDicCurrencyRepository
    {
        private const string Columns = "Uid, Version, IsDeleted, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy, CurrencyCode, CurrencyNameEn, CurrencyNameZh, CurrencySymbol, Precision, RateToBase, IsBase, IsSystem, IsEnabled, Remark";

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

        public async Task<PagedResponseBase<SysDicCurrencyDto>> GetPagedAsync(SysDicCurrencySearchRequest request)
        {
            request ??= new SysDicCurrencySearchRequest();
            var pageIndex = Math.Max(1, request.PageIndex);
            var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
            var parameters = new DynamicParameters(new { Offset = (pageIndex - 1) * pageSize, PageSize = pageSize });
            var filters = new List<string> { "(IsDeleted = 0 OR IsDeleted IS NULL)" };
            if (!string.IsNullOrWhiteSpace(request.Keyword))
            {
                filters.Add("(CurrencyCode LIKE @Keyword OR CurrencyNameEn LIKE @Keyword OR CurrencyNameZh LIKE @Keyword OR CurrencySymbol LIKE @Keyword)");
                parameters.Add("Keyword", $"%{request.Keyword.Trim()}%");
            }

            var batchKeywords = request.BatchKeywords?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
            if (batchKeywords.Length > 0)
            {
                filters.Add("CurrencyCode IN @BatchKeywords");
                parameters.Add("BatchKeywords", batchKeywords);
            }

            var where = string.Join(" AND ", filters);
            var total = await Connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(1) FROM dbo.SysDicCurrency WHERE {where}", parameters, Transaction);
            var rows = await Connection.QueryAsync<SysDicCurrencyDto>($@"
                SELECT {Columns}
                FROM dbo.SysDicCurrency
                WHERE {where}
                ORDER BY IsBase DESC, CurrencyCode, Uid
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", parameters, Transaction);
            return new PagedResponseBase<SysDicCurrencyDto>
            {
                Items = rows.AsList(),
                TotalCount = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }

        public async Task<IReadOnlyList<SysDicCurrency>> GetAllForUpdateAsync()
        {
            var rows = await Connection.QueryAsync<SysDicCurrency>(
                $"SELECT {Columns} FROM dbo.SysDicCurrency WITH (UPDLOCK, ROWLOCK) WHERE IsDeleted = 0 OR IsDeleted IS NULL",
                transaction: Transaction);
            return rows.AsList();
        }

        public async Task<IReadOnlyList<SysDicCurrency>> GetByCurrencyCodesAsync(
            IEnumerable<string> currencyCodes,
            bool withUpdLock = false)
        {
            var codes = currencyCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (codes.Length == 0) return [];
            var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";
            var rows = await Connection.QueryAsync<SysDicCurrency>(
                $"SELECT {Columns} FROM dbo.SysDicCurrency {lockHint} WHERE CurrencyCode IN @Codes AND (IsDeleted = 0 OR IsDeleted IS NULL)",
                new { Codes = codes }, Transaction);
            return rows.AsList();
        }

        public async Task<IReadOnlyList<SysDicCurrency>> GetBaseCurrenciesAsync()
        {
            var rows = await Connection.QueryAsync<SysDicCurrency>(
                $"SELECT {Columns} FROM dbo.SysDicCurrency WITH (NOLOCK) WHERE IsBase = 1 AND (IsDeleted = 0 OR IsDeleted IS NULL) ORDER BY CurrencyCode, Uid",
                transaction: Transaction);
            return rows.AsList();
        }

        public async Task<IReadOnlyList<SysDicCurrency>> GetEnabledCurrenciesAsync()
        {
            var rows = await Connection.QueryAsync<SysDicCurrency>(
                $"SELECT {Columns} FROM dbo.SysDicCurrency WITH (NOLOCK) WHERE IsEnabled = 1 AND (IsDeleted = 0 OR IsDeleted IS NULL) ORDER BY IsBase DESC, CurrencyCode, Uid",
                transaction: Transaction);
            return rows.AsList();
        }

        public async Task<IReadOnlyList<string>> GetBusinessReferencedCurrencyCodesAsync(IEnumerable<string> currencyCodes)
        {
            var codes = currencyCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (codes.Length == 0) return [];
            var rows = await Connection.QueryAsync<string>(@"
                SELECT DISTINCT CurrencyCode
                FROM
                (
                    SELECT Currency AS CurrencyCode FROM dbo.Customer WITH (UPDLOCK, ROWLOCK)
                    WHERE Currency IN @Codes AND (IsDeleted = 0 OR IsDeleted IS NULL)
                    UNION ALL
                    SELECT Currency FROM dbo.Supplier WITH (UPDLOCK, ROWLOCK)
                    WHERE Currency IN @Codes AND (IsDeleted = 0 OR IsDeleted IS NULL)
                    UNION ALL
                    SELECT Currency FROM dbo.CustomerPoConfig WITH (UPDLOCK, ROWLOCK)
                    WHERE Currency IN @Codes AND (IsDeleted = 0 OR IsDeleted IS NULL)
                    UNION ALL
                    SELECT Currency FROM dbo.SalesOrderHeader WITH (UPDLOCK, ROWLOCK)
                    WHERE Currency IN @Codes AND (IsDeleted = 0 OR IsDeleted IS NULL)
                    UNION ALL
                    SELECT Currency FROM dbo.PurchaseOrderHead WITH (UPDLOCK, ROWLOCK)
                    WHERE Currency IN @Codes AND (IsDeleted = 0 OR IsDeleted IS NULL)
                ) referenced
                WHERE CurrencyCode IS NOT NULL;", new { Codes = codes }, Transaction);
            return rows.AsList();
        }

    }
}
