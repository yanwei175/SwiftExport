using Dapper;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Suppliers.Management;

/// <summary>供应商 PostgreSQL 实现；共享调用方事务，编码比较暂借已部署字典排序规则，不代表唯一索引已适配。</summary>
public sealed class PostgreSqlSupplierRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<Supplier>(context), ISupplierRepository
{
    private static readonly string Table = $"{PostgreSqlNames.Quote(Metadata.SchemaName)}.{PostgreSqlNames.Quote(Metadata.TableName)}";
    private static readonly string EntityColumns = string.Join(", ", Metadata.Columns.Select(c =>
        $"s.{PostgreSqlNames.Quote(c.ColumnName)} AS {PostgreSqlNames.Quote(c.PropertyName)}"));
    private static readonly string DtoColumns = string.Join(", ", Metadata.Columns
        .Where(c => c.PropertyName != nameof(Supplier.IsActive)).Select(c =>
            $"s.{PostgreSqlNames.Quote(c.ColumnName)} AS {PostgreSqlNames.Quote(c.PropertyName)}"));
    private const string CodeValue = "rtrim(s.supplier_code, ' ') COLLATE public.erp_dictionary_ci";
    private const string MatchesCodes = "EXISTS (SELECT 1 FROM unnest(@Codes) AS requested(code) WHERE rtrim(requested.code, ' ') COLLATE public.erp_dictionary_ci = " + CodeValue + ")";

    /// <summary>普通资料更新不写启停资格，沿用独立生命周期命令形成的状态。</summary>
    protected override IReadOnlyList<string> UpdateProperties => [.. base.UpdateProperties
        .Where(p => !p.Equals(nameof(Supplier.IsActive), StringComparison.OrdinalIgnoreCase))];

    /// <summary>编码存在检查包含软删记录；不修剪前导空格，保留旧入口边界。</summary>
    public Task<bool> ExistBySupplierCode(string supplierCode)
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("Code", supplierCode);
        return Connection.ExecuteScalarAsync<bool>($"SELECT EXISTS (SELECT 1 FROM {Table} s WHERE {CodeValue} = rtrim(CAST(@Code AS text), ' ') COLLATE public.erp_dictionary_ci)", parameters, Transaction);
    }

    /// <summary>输入先 Trim，返回首个未软删供应商；停用供应商仍可读取，空输入不查询。</summary>
    public async Task<Supplier?> GetByCodeAsync(string supplierCode)
    {
        if (string.IsNullOrWhiteSpace(supplierCode)) return null;
        var parameters = new PostgreSqlParameters();
        parameters.Add("Code", supplierCode.Trim());
        return await Connection.QueryFirstOrDefaultAsync<Supplier>($"SELECT {EntityColumns} FROM {Table} s WHERE {CodeValue} = rtrim(CAST(@Code AS text), ' ') COLLATE public.erp_dictionary_ci AND (s.is_deleted = FALSE OR s.is_deleted IS NULL) LIMIT 1", parameters, Transaction);
    }

    /// <summary>返回数据库原编码，包含软删记录且不额外去重；空集合无需查询。</summary>
    public async Task<IReadOnlyList<string>> GetExistingSupplierCodesAsync(IEnumerable<string> supplierCodeList)
    {
        var codes = supplierCodeList.ToArray();
        if (codes.Length == 0) return [];
        return (await Connection.QueryAsync<string>($"SELECT s.supplier_code FROM {Table} s WHERE {MatchesCodes}", Codes(codes), Transaction)).AsList();
    }

    /// <summary>排除整批当前身份，包含软删占用者；按源比较规则去重，返回选中记录的原文本。</summary>
    public async Task<IReadOnlyList<string>> GetSupplierCodesUsedByOtherSuppliersAsync(IEnumerable<(Guid Uid, string SupplierCode)> items)
    {
        var values = items.ToArray();
        if (values.Length == 0) return [];
        var parameters = PostgreSqlParameters.Ids(values.Select(x => x.Uid));
        parameters.AddTexts("Codes", values.Select(x => x.SupplierCode));
        // DISTINCT ON 的键采用相同编码比较；不能把规范化结果作为业务编码返回。
        return (await Connection.QueryAsync<string>($"SELECT DISTINCT ON ({CodeValue}) s.supplier_code FROM {Table} s WHERE {MatchesCodes} AND NOT (s.uid = ANY(@Uids)) ORDER BY {CodeValue}, s.uid", parameters, Transaction)).AsList();
    }

    /// <summary>过滤空白、Trim 并以 OrdinalIgnoreCase 合并输入，查询未软删供应商且保留停用状态。</summary>
    public async Task<IReadOnlyList<Supplier>> GetBySupplierCodeListAsync(IEnumerable<string> supplierCodeList)
    {
        if (supplierCodeList is null) return [];
        var codes = supplierCodeList.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (codes.Length == 0) return [];
        return (await Connection.QueryAsync<Supplier>($"SELECT {EntityColumns} FROM {Table} s WHERE {MatchesCodes} AND (s.is_deleted = FALSE OR s.is_deleted IS NULL)", Codes(codes), Transaction)).AsList();
    }

    /// <summary>计数和分页使用原 DTO 投影及通配规则，排除软删但不排除停用供应商；不建立新事务。</summary>
    public async Task<PagedResponseBase<SupplierDto>> GetPagedAsync(SupplierSearchRequest request)
    {
        request ??= new SupplierSearchRequest();
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Min(request.PageSize <= 0 ? 20 : request.PageSize, 200);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        var where = "(s.is_deleted = FALSE OR s.is_deleted IS NULL)";
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            where += " AND (s.supplier_code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR s.supplier_name COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR s.doc_relate_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR s.contact_person COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')";
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
        }
        var total = await Connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {Table} s WHERE {where}", parameters, Transaction);
        var rows = await Connection.QueryAsync<SupplierDto>($"SELECT {DtoColumns} FROM {Table} s WHERE {where} ORDER BY s.supplier_code COLLATE public.erp_dictionary_ci, s.uid LIMIT @PageSize OFFSET @Offset", parameters, Transaction);
        return new PagedResponseBase<SupplierDto> { Items = rows.AsList(), TotalCount = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>将编码作为单个 text 数组绑定，避免 Dapper 展开为 IN 参数。</summary>
    private static PostgreSqlParameters Codes(IEnumerable<string> values)
    {
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("Codes", values);
        return parameters;
    }
}
