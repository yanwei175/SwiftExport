using SwiftExport.Core.Interfaces.Systems.ExchangeRates;
using Dapper;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.ExchangeRates;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems.ExchangeRates
{
    /// <summary>
    /// 货币汇率字典仓储实现
    /// </summary>
    public class SysDicCurrencyExchangeRepository(RepositoryBaseContext context)
        : DapperRepository<SysDicCurrencyExchange>(context), ISysDicCurrencyExchangeRepository
    {
        private const string Columns = "Uid, Version, IsDeleted, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy, IsEnabled, FromCurrencyCode, ToCurrencyCode, Precision, Rate, Remark";

        public async Task<PagedResponseBase<SysDicCurrencyExchangeDto>> GetPagedAsync(SysDicCurrencyExchangeSearchRequest request)
        {
            request ??= new SysDicCurrencyExchangeSearchRequest();
            var pageIndex = Math.Max(1, request.PageIndex);
            var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
            var parameters = new DynamicParameters(new { Offset = (pageIndex - 1) * pageSize, PageSize = pageSize });
            var where = "(IsDeleted = 0 OR IsDeleted IS NULL)";
            if (!string.IsNullOrWhiteSpace(request.Keyword))
            {
                where += " AND (FromCurrencyCode LIKE @Keyword OR ToCurrencyCode LIKE @Keyword OR Remark LIKE @Keyword)";
                parameters.Add("Keyword", $"%{request.Keyword.Trim()}%");
            }

            var total = await Connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(1) FROM dbo.SysDicCurrencyExchange WHERE {where}", parameters, Transaction);
            var rows = await Connection.QueryAsync<SysDicCurrencyExchangeDto>($@"
                SELECT {Columns}
                FROM dbo.SysDicCurrencyExchange
                WHERE {where}
                ORDER BY FromCurrencyCode, ToCurrencyCode, Uid
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", parameters, Transaction);
            return new PagedResponseBase<SysDicCurrencyExchangeDto>
            {
                Items = rows.AsList(),
                TotalCount = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }

        public async Task<IReadOnlyList<SysDicCurrencyExchange>> GetAllForUpdateAsync()
        {
            var rows = await Connection.QueryAsync<SysDicCurrencyExchange>(
                $"SELECT {Columns} FROM dbo.SysDicCurrencyExchange WITH (UPDLOCK, ROWLOCK) WHERE IsDeleted = 0 OR IsDeleted IS NULL",
                transaction: Transaction);
            return rows.AsList();
        }

        public async Task<IReadOnlyList<string>> GetReferencedCurrencyCodesAsync(IEnumerable<string> currencyCodes)
        {
            var codes = currencyCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (codes.Length == 0) return [];
            var rows = await Connection.QueryAsync<string>(@"
                SELECT DISTINCT CurrencyCode
                FROM
                (
                    SELECT FromCurrencyCode AS CurrencyCode
                    FROM dbo.SysDicCurrencyExchange WITH (UPDLOCK, ROWLOCK)
                    WHERE FromCurrencyCode IN @Codes AND (IsDeleted = 0 OR IsDeleted IS NULL)
                    UNION ALL
                    SELECT ToCurrencyCode AS CurrencyCode
                    FROM dbo.SysDicCurrencyExchange WITH (UPDLOCK, ROWLOCK)
                    WHERE ToCurrencyCode IN @Codes AND (IsDeleted = 0 OR IsDeleted IS NULL)
                ) referenced
                WHERE CurrencyCode IS NOT NULL;", new { Codes = codes }, Transaction);
            return rows.AsList();
        }
    }
}
