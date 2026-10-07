using Dapper;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Entities.Systems.Currencies;
using SwiftExport.Core.Interfaces.Systems.Currencies;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems.Currencies;

/// <summary>币种完整 PostgreSQL 仓储；复用泛型 CRUD 和外层会话，不接管业务事务或本位币约束。</summary>
public sealed class PostgreSqlSysDicCurrencyRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<SysDicCurrency>(context), ISysDicCurrencyRepository
{
    private const string Columns = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "currency_code" AS "CurrencyCode",
        "currency_name_en" AS "CurrencyNameEn",
        "currency_name_zh" AS "CurrencyNameZh",
        "currency_symbol" AS "CurrencySymbol",
        "precision" AS "Precision",
        "rate_to_base" AS "RateToBase",
        "is_base" AS "IsBase",
        "is_system" AS "IsSystem",
        "is_enabled" AS "IsEnabled",
        "remark" AS "Remark"
    """;
    private const string Ci = "public.erp_dictionary_ci";
    private static readonly string Active = $"(\"is_deleted\"=FALSE OR \"is_deleted\" IS NULL)";
    private static readonly string CodeOrder = $"rtrim(\"currency_code\", ' ') COLLATE {Ci} NULLS FIRST, {UuidOrder("\"uid\"")}";

    /// <summary>按切换规则取消所有本位币标识并将汇率重置为1:1；共享外层事务，不更新版本或审计字段。</summary>
    public async Task<bool> ResetAllBaseCurrencyAsync()
    {
        await Connection.ExecuteAsync($"UPDATE \"public\".\"sys_dic_currency\" SET \"rate_to_base\"=1, \"is_base\"=FALSE",
            transaction: Transaction);
        return true;
    }

    /// <summary>查询未删除的本位币 ID，按调用方给定集合排除；本入口不接受删除标识为空的行。</summary>
    public async Task<IReadOnlyList<Guid>> GetBaseIsBaseCurrencyIdsAsync(IEnumerable<Guid>? excludeIds = null)
    {
        var ids = excludeIds?.ToArray() ?? [];
        var parameters = new PostgreSqlParameters();
        var excluded = "";
        if (ids.Length > 0)
        {
            parameters.AddIds("ExcludeIds", ids);
            excluded = $" AND NOT (\"uid\"=ANY(@ExcludeIds))";
        }
        var rows = await Connection.QueryAsync<Guid>($"SELECT \"uid\" FROM \"public\".\"sys_dic_currency\" WHERE \"is_base\"=TRUE AND \"is_deleted\"=FALSE{excluded}",
            parameters, Transaction);
        return rows.AsList();
    }

    /// <summary>查询已有代码，保留输入中的空值、空白及重复项语义，不额外规范化或去重返回行。</summary>
    public async Task<IReadOnlyList<string>> GetExistingCurrencyCodesAsync(IEnumerable<string> currencyCodes)
    {
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("Codes", currencyCodes);
        var rows = await Connection.QueryAsync<string>($"SELECT \"currency_code\" FROM \"public\".\"sys_dic_currency\" WHERE {MatchesCodes("\"currency_code\"", "Codes")} AND \"is_deleted\"=FALSE",
            parameters, Transaction);
        return rows.AsList();
    }

    /// <summary>按原分页上下限、四字段关键字和批量代码查询；计数及取页沿用当前事务分别执行。</summary>
    public async Task<PagedResponseBase<SysDicCurrencyDto>> GetPagedAsync(SysDicCurrencySearchRequest request)
    {
        request ??= new SysDicCurrencySearchRequest();
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        var filters = new List<string> { Active };
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            // 源入口使用裸 LIKE；不能调用会转义百分号、转换问号和空格的 SqlLikePattern.Build。
            parameters.Add("Keyword", $"%{request.Keyword.Trim()}%");
            filters.Add($"""
                ("currency_code" COLLATE {Ci} LIKE @Keyword ESCAPE ''
                 OR "currency_name_en" COLLATE {Ci} LIKE @Keyword ESCAPE ''
                 OR "currency_name_zh" COLLATE {Ci} LIKE @Keyword ESCAPE ''
                 OR "currency_symbol" COLLATE {Ci} LIKE @Keyword ESCAPE '')
                """);
        }
        var codes = NormalizeCodes(request.BatchKeywords ?? []);
        if (codes.Length > 0)
        {
            parameters.AddTexts("BatchKeywords", codes);
            filters.Add(MatchesCodes("\"currency_code\"", "BatchKeywords"));
        }
        var where = string.Join(" AND ", filters);
        var total = await Connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM \"public\".\"sys_dic_currency\" WHERE {where}", parameters, Transaction);
        var rows = await Connection.QueryAsync<SysDicCurrencyDto>($"SELECT {Columns} FROM \"public\".\"sys_dic_currency\" WHERE {where} ORDER BY \"is_base\" DESC NULLS LAST, {CodeOrder} LIMIT @PageSize OFFSET @Offset",
            parameters, Transaction);
        return new PagedResponseBase<SysDicCurrencyDto> { Items = rows.AsList(), TotalCount = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>锁定全部有效币种已有行；要求调用方已有事务，不提供缺失行或范围保护。</summary>
    public async Task<IReadOnlyList<SysDicCurrency>> GetAllForUpdateAsync()
    {
        var lockHint = GetLockHint(true);
        var rows = await Connection.QueryAsync<SysDicCurrency>($"SELECT {Columns} FROM \"public\".\"sys_dic_currency\" WHERE {Active}{lockHint}", transaction: Transaction);
        return rows.AsList();
    }

    /// <summary>按原去空白、Trim 和忽略大小写去重规则查询币种；可选更新锁只保护已有行。</summary>
    public async Task<IReadOnlyList<SysDicCurrency>> GetByCurrencyCodesAsync(IEnumerable<string> currencyCodes, bool withUpdLock = false)
    {
        var codes = NormalizeCodes(currencyCodes);
        if (codes.Length == 0) return [];
        var lockHint = GetLockHint(withUpdLock);
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("Codes", codes);
        var rows = await Connection.QueryAsync<SysDicCurrency>($"SELECT {Columns} FROM \"public\".\"sys_dic_currency\" WHERE {MatchesCodes("\"currency_code\"", "Codes")} AND {Active}{lockHint}", parameters, Transaction);
        return rows.AsList();
    }

    /// <summary>按代码和 Uid 返回有效本位币；普通读取使用 PostgreSQL 当前事务快照。</summary>
    public async Task<IReadOnlyList<SysDicCurrency>> GetBaseCurrenciesAsync()
    {
        var rows = await Connection.QueryAsync<SysDicCurrency>($"SELECT {Columns} FROM \"public\".\"sys_dic_currency\" WHERE \"is_base\"=TRUE AND {Active} ORDER BY {CodeOrder}", transaction: Transaction);
        return rows.AsList();
    }

    /// <summary>返回有效且启用的币种，本位币优先，随后按代码和 Uid 排序。</summary>
    public async Task<IReadOnlyList<SysDicCurrency>> GetEnabledCurrenciesAsync()
    {
        var rows = await Connection.QueryAsync<SysDicCurrency>($"SELECT {Columns} FROM \"public\".\"sys_dic_currency\" WHERE \"is_enabled\"=TRUE AND {Active} ORDER BY \"is_base\" DESC NULLS LAST, {CodeOrder}", transaction: Transaction);
        return rows.AsList();
    }

    /// <summary>查询五类业务已有引用并加行锁，在数据库按既有 CI 和尾空格规则去重，返回原始代码文本。</summary>
    public async Task<IReadOnlyList<string>> GetBusinessReferencedCurrencyCodesAsync(IEnumerable<string> currencyCodes)
    {
        var codes = NormalizeCodes(currencyCodes);
        if (codes.Length == 0) return [];
        var lockHint = GetLockHint(true);
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("Codes", codes);
        // 行锁保留在每个来源表内；UNION / DISTINCT 只汇总已锁定的引用代码。
        var sql = $"""
            WITH
            reference_0 AS MATERIALIZED (
                SELECT "currency" AS code FROM "public"."customer"
                WHERE {MatchesCodes("\"currency\"", "Codes")}
                  AND ("is_deleted"=FALSE OR "is_deleted" IS NULL){lockHint}
            ),
            reference_1 AS MATERIALIZED (
                SELECT "currency" AS code FROM "public"."supplier"
                WHERE {MatchesCodes("\"currency\"", "Codes")}
                  AND ("is_deleted"=FALSE OR "is_deleted" IS NULL){lockHint}
            ),
            reference_2 AS MATERIALIZED (
                SELECT "currency" AS code FROM "public"."customer_po_config"
                WHERE {MatchesCodes("\"currency\"", "Codes")}
                  AND ("is_deleted"=FALSE OR "is_deleted" IS NULL){lockHint}
            ),
            reference_3 AS MATERIALIZED (
                SELECT "currency" AS code FROM "public"."sales_order_header"
                WHERE {MatchesCodes("\"currency\"", "Codes")}
                  AND ("is_deleted"=FALSE OR "is_deleted" IS NULL){lockHint}
            ),
            reference_4 AS MATERIALIZED (
                SELECT "currency" AS code FROM "public"."purchase_order_head"
                WHERE {MatchesCodes("\"currency\"", "Codes")}
                  AND ("is_deleted"=FALSE OR "is_deleted" IS NULL){lockHint}
            )
            SELECT DISTINCT ON (rtrim(code, ' ') COLLATE {Ci}) code
            FROM (SELECT code FROM reference_0 UNION ALL SELECT code FROM reference_1
                  UNION ALL SELECT code FROM reference_2 UNION ALL SELECT code FROM reference_3
                  UNION ALL SELECT code FROM reference_4) referenced WHERE code IS NOT NULL
            """;
        var rows = await Connection.QueryAsync<string>(sql, parameters, Transaction);
        return rows.AsList();
    }

    /// <summary>同代码行保留源 uniqueidentifier 的字节权重，不用 PostgreSQL 原生 UUID 顺序。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(index => $"get_byte(uuid_send({column}),{index})")) + "]";

    /// <summary>仅复用既有 ASCII 尾空格和 CI 比较表达式；输入数组不改变，前导空格保留。</summary>
    private static string MatchesCodes(string column, string parameter) =>
        $"EXISTS (SELECT 1 FROM unnest(@{parameter}) input(code) WHERE rtrim({column}, ' ') COLLATE {Ci}=rtrim(input.code, ' ') COLLATE {Ci})";

    /// <summary>只用于源代码明确规范化的入口；已有代码查询不会经过此路径。</summary>
    private static string[] NormalizeCodes(IEnumerable<string> codes) => codes.Where(code => !string.IsNullOrWhiteSpace(code))
        .Select(code => code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
