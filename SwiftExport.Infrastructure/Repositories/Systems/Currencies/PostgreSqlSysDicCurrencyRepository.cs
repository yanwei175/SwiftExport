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
    private static readonly string Table = Qualified(Metadata);
    private static readonly string Columns = string.Join(", ", Metadata.Columns.Select(column =>
        $"{PostgreSqlNames.Quote(column.ColumnName)} AS {PostgreSqlNames.Quote(column.PropertyName)}"));
    private const string Ci = "public.erp_dictionary_ci";
    private static readonly string Active = $"({Column(nameof(SysDicCurrency.IsDeleted))}=FALSE OR {Column(nameof(SysDicCurrency.IsDeleted))} IS NULL)";
    private static readonly string CodeOrder = $"rtrim({Column(nameof(SysDicCurrency.CurrencyCode))}, ' ') COLLATE {Ci} NULLS FIRST, {UuidOrder(Column(nameof(SysDicCurrency.Uid)))}";

    /// <summary>按切换规则取消所有本位币标识并将汇率重置为1:1；共享外层事务，不更新版本或审计字段。</summary>
    public async Task<bool> ResetAllBaseCurrencyAsync()
    {
        await Connection.ExecuteAsync($"UPDATE {Table} SET {Column(nameof(SysDicCurrency.RateToBase))}=1, {Column(nameof(SysDicCurrency.IsBase))}=FALSE",
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
            excluded = $" AND NOT ({Column(nameof(SysDicCurrency.Uid))}=ANY(@ExcludeIds))";
        }
        var rows = await Connection.QueryAsync<Guid>($"SELECT {Column(nameof(SysDicCurrency.Uid))} FROM {Table} WHERE {Column(nameof(SysDicCurrency.IsBase))}=TRUE AND {Column(nameof(SysDicCurrency.IsDeleted))}=FALSE{excluded}",
            parameters, Transaction);
        return rows.AsList();
    }

    /// <summary>查询已有代码，保留输入中的空值、空白及重复项语义，不额外规范化或去重返回行。</summary>
    public async Task<IReadOnlyList<string>> GetExistingCurrencyCodesAsync(IEnumerable<string> currencyCodes)
    {
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("Codes", currencyCodes);
        var rows = await Connection.QueryAsync<string>($"SELECT {Column(nameof(SysDicCurrency.CurrencyCode))} FROM {Table} WHERE {MatchesCodes(Column(nameof(SysDicCurrency.CurrencyCode)), "Codes")} AND {Column(nameof(SysDicCurrency.IsDeleted))}=FALSE",
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
            filters.Add("(" + string.Join(" OR ", new[] { nameof(SysDicCurrency.CurrencyCode), nameof(SysDicCurrency.CurrencyNameEn), nameof(SysDicCurrency.CurrencyNameZh), nameof(SysDicCurrency.CurrencySymbol) }
                .Select(property => $"{Column(property)} COLLATE {Ci} LIKE @Keyword ESCAPE ''")) + ")");
        }
        var codes = NormalizeCodes(request.BatchKeywords ?? []);
        if (codes.Length > 0)
        {
            parameters.AddTexts("BatchKeywords", codes);
            filters.Add(MatchesCodes(Column(nameof(SysDicCurrency.CurrencyCode)), "BatchKeywords"));
        }
        var where = string.Join(" AND ", filters);
        var total = await Connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {Table} WHERE {where}", parameters, Transaction);
        var rows = await Connection.QueryAsync<SysDicCurrencyDto>($"SELECT {Columns} FROM {Table} WHERE {where} ORDER BY {Column(nameof(SysDicCurrency.IsBase))} DESC NULLS LAST, {CodeOrder} LIMIT @PageSize OFFSET @Offset",
            parameters, Transaction);
        return new PagedResponseBase<SysDicCurrencyDto> { Items = rows.AsList(), TotalCount = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>锁定全部有效币种已有行；要求调用方已有事务，不提供缺失行或范围保护。</summary>
    public async Task<IReadOnlyList<SysDicCurrency>> GetAllForUpdateAsync()
    {
        var lockHint = GetLockHint(true);
        var rows = await Connection.QueryAsync<SysDicCurrency>($"SELECT {Columns} FROM {Table} WHERE {Active}{lockHint}", transaction: Transaction);
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
        var rows = await Connection.QueryAsync<SysDicCurrency>($"SELECT {Columns} FROM {Table} WHERE {MatchesCodes(Column(nameof(SysDicCurrency.CurrencyCode)), "Codes")} AND {Active}{lockHint}", parameters, Transaction);
        return rows.AsList();
    }

    /// <summary>按代码和 Uid 返回有效本位币；普通读取使用 PostgreSQL 当前事务快照。</summary>
    public async Task<IReadOnlyList<SysDicCurrency>> GetBaseCurrenciesAsync()
    {
        var rows = await Connection.QueryAsync<SysDicCurrency>($"SELECT {Columns} FROM {Table} WHERE {Column(nameof(SysDicCurrency.IsBase))}=TRUE AND {Active} ORDER BY {CodeOrder}", transaction: Transaction);
        return rows.AsList();
    }

    /// <summary>返回有效且启用的币种，本位币优先，随后按代码和 Uid 排序。</summary>
    public async Task<IReadOnlyList<SysDicCurrency>> GetEnabledCurrenciesAsync()
    {
        var rows = await Connection.QueryAsync<SysDicCurrency>($"SELECT {Columns} FROM {Table} WHERE {Column(nameof(SysDicCurrency.IsEnabled))}=TRUE AND {Active} ORDER BY {Column(nameof(SysDicCurrency.IsBase))} DESC NULLS LAST, {CodeOrder}", transaction: Transaction);
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
        var types = new[] { typeof(Customer), typeof(Supplier), typeof(CustomerPoConfig), typeof(SalesOrderHeader), typeof(PurchaseOrderHead) };
        var sources = types.Select((type, index) =>
        {
            var metadata = PostgreSqlEntityMetadata.GetMetadata(type);
            var currency = PostgreSqlNames.Quote(metadata.GetProperty("Currency").ColumnName);
            var deleted = PostgreSqlNames.Quote(metadata.GetProperty("IsDeleted").ColumnName);
            return $"reference_{index} AS MATERIALIZED (SELECT {currency} AS code FROM {Qualified(metadata)} WHERE {MatchesCodes(currency, "Codes")} AND ({deleted}=FALSE OR {deleted} IS NULL){lockHint})";
        });
        // 锁放在每个源表查询中，避免在 UNION / DISTINCT 上使用 PostgreSQL 不支持的 FOR UPDATE。
        var union = string.Join(" UNION ALL ", Enumerable.Range(0, types.Length).Select(index => $"SELECT code FROM reference_{index}"));
        var sql = $"WITH {string.Join(", ", sources)} SELECT DISTINCT ON (rtrim(code, ' ') COLLATE {Ci}) code FROM ({union}) referenced WHERE code IS NOT NULL";
        var rows = await Connection.QueryAsync<string>(sql, parameters, Transaction);
        return rows.AsList();
    }

    /// <summary>限定元数据表名，避免依赖 search_path 或修改共享名称映射。</summary>
    private static string Qualified(EntityMetadata metadata) => $"{PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)}";

    private static string Column(string property) => PostgreSqlNames.Quote(Metadata.GetProperty(property).ColumnName);

    /// <summary>同代码行保留源 uniqueidentifier 的字节权重，不用 PostgreSQL 原生 UUID 顺序。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(index => $"get_byte(uuid_send({column}),{index})")) + "]";

    /// <summary>仅复用既有 ASCII 尾空格和 CI 比较表达式；输入数组不改变，前导空格保留。</summary>
    private static string MatchesCodes(string column, string parameter) =>
        $"EXISTS (SELECT 1 FROM unnest(@{parameter}) input(code) WHERE rtrim({column}, ' ') COLLATE {Ci}=rtrim(input.code, ' ') COLLATE {Ci})";

    /// <summary>只用于源代码明确规范化的入口；已有代码查询不会经过此路径。</summary>
    private static string[] NormalizeCodes(IEnumerable<string> codes) => codes.Where(code => !string.IsNullOrWhiteSpace(code))
        .Select(code => code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
