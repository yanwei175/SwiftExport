using Dapper;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.ExchangeRates;
using SwiftExport.Core.Interfaces.Systems.ExchangeRates;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems.ExchangeRates;

/// <summary>PostgreSQL 汇率仓储；共享调用方会话与事务，保留原接口及默认 SQL Server 注册。</summary>
public sealed class PostgreSqlSysDicCurrencyExchangeRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<SysDicCurrencyExchange>(context), ISysDicCurrencyExchangeRepository
{
    private const string Columns = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "is_enabled" AS "IsEnabled",
        "from_currency_code" AS "FromCurrencyCode",
        "to_currency_code" AS "ToCurrencyCode",
        "precision" AS "Precision",
        "rate" AS "Rate",
        "remark" AS "Remark"
    """;

    /// <summary>按源币种、目标币种及备注查询有效汇率，返回稳定分页及共享契约投影。</summary>
    public async Task<PagedResponseBase<SysDicCurrencyExchangeDto>> GetPagedAsync(SysDicCurrencyExchangeSearchRequest request)
    {
        request ??= new SysDicCurrencyExchangeSearchRequest();
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        var where = "(is_deleted = FALSE OR is_deleted IS NULL)";
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            // 沿用字典的通配符和反斜杠规则；方括号模式与源库的差异仍待比较规则验收。
            where += " AND (from_currency_code LIKE @Keyword ESCAPE '' OR to_currency_code LIKE @Keyword ESCAPE '' OR remark LIKE @Keyword ESCAPE '')";
            parameters.Add("Keyword", $"%{request.Keyword.Trim()}%");
        }

        var total = await Connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM public.sys_dic_currency_exchange WHERE {where}", parameters, Transaction);
        var rows = await Connection.QueryAsync<SysDicCurrencyExchangeDto>($@"
            SELECT {Columns}
            FROM public.sys_dic_currency_exchange
            WHERE {where}
            ORDER BY from_currency_code NULLS FIRST, to_currency_code NULLS FIRST, uid
            LIMIT @PageSize OFFSET @Offset;", parameters, Transaction);
        return new PagedResponseBase<SysDicCurrencyExchangeDto>
        {
            Items = rows.AsList(),
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    /// <summary>在已有事务中按 Uid 锁定全部有效汇率行；不锁不存在的关系或自行开启事务。</summary>
    public async Task<IReadOnlyList<SysDicCurrencyExchange>> GetAllForUpdateAsync()
    {
        var lockHint = GetLockHint(true);
        var rows = await Connection.QueryAsync<SysDicCurrencyExchange>(
            $"SELECT {Columns} FROM public.sys_dic_currency_exchange WHERE is_deleted = FALSE OR is_deleted IS NULL ORDER BY uid{lockHint}",
            transaction: Transaction);
        return rows.AsList();
    }

    /// <summary>锁定引用指定币种的有效汇率行，再返回命中的编码；停用关系仍阻断改编码或删除。</summary>
    public async Task<IReadOnlyList<string>> GetReferencedCurrencyCodesAsync(IEnumerable<string> currencyCodes)
    {
        var codes = currencyCodes.Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (codes.Length == 0) return [];
        var lockHint = GetLockHint(true);
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("Codes", codes);
        // CASE 使用相同 SQL 比较条件，只投影请求命中的端点；先锁原始行，避免 DISTINCT/UNION 锁结果集。
        var rows = await Connection.QueryAsync<SysDicCurrencyExchange>($@"
            SELECT uid AS ""Uid"",
                CASE WHEN rtrim(from_currency_code, ' ') = ANY(@Codes) THEN from_currency_code END AS ""FromCurrencyCode"",
                CASE WHEN rtrim(to_currency_code, ' ') = ANY(@Codes) THEN to_currency_code END AS ""ToCurrencyCode""
            FROM public.sys_dic_currency_exchange
            WHERE (rtrim(from_currency_code, ' ') = ANY(@Codes) OR rtrim(to_currency_code, ' ') = ANY(@Codes))
                AND (is_deleted = FALSE OR is_deleted IS NULL)
            ORDER BY uid{lockHint};", parameters, Transaction);
        return rows.SelectMany(row => new[] { row.FromCurrencyCode, row.ToCurrencyCode })
            .Where(code => code is not null).Select(code => code!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
