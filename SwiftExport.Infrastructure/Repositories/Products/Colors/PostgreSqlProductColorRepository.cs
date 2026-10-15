using Dapper;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.Colors;

/// <summary>颜色 PostgreSQL 仓储；在调用方事务查询有效产品及中性产品引用，颜色父行由删除入口先锁定。</summary>
public sealed class PostgreSqlProductColorRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<ProductColor>(context), IProductColorRepository
{
    private static readonly string Table = $"{PostgreSqlNames.Quote(Metadata.SchemaName)}.{PostgreSqlNames.Quote(Metadata.TableName)}";
    private static readonly EntityMetadata ProductMetadata = EntityMetadataCache.GetMetadata(typeof(Product));
    private static readonly string ProductTable = $"{PostgreSqlNames.Quote(ProductMetadata.SchemaName)}.{PostgreSqlNames.Quote(ProductMetadata.TableName)}";
    private const string Columns = """
        c."uid" AS "Uid",
        c."version" AS "Version",
        c."is_deleted" AS "IsDeleted",
        c."created_date" AS "CreatedDate",
        c."created_by" AS "CreatedBy",
        c."updated_date" AS "UpdatedDate",
        c."updated_by" AS "UpdatedBy",
        c."color_code_unique" AS "ColorCodeUnique",
        c."color_code" AS "ColorCode",
        c."color_name_en" AS "ColorNameEn",
        c."color_name_zh" AS "ColorNameZh",
        c."sort_no" AS "SortNo"
    """;
    private const string NarrowColumns = """
        c."uid" AS "Uid",
        c."version" AS "Version",
        c."is_deleted" AS "IsDeleted",
        c."color_code_unique" AS "ColorCodeUnique",
        c."color_code" AS "ColorCode",
        c."color_name_en" AS "ColorNameEn",
        c."color_name_zh" AS "ColorNameZh",
        c."sort_no" AS "SortNo"
    """;

    private const string DisplayOrder = "c.sort_no ASC, c.color_code_unique COLLATE public.erp_dictionary_ci ASC NULLS FIRST, c.uid";

    /// <summary>颜色完整列表使用专属显示顺序，不改变泛型仓储和其他实体的默认排序。</summary>
    public override async Task<IReadOnlyList<ProductColor>> GetAllAsync() =>
        (await Connection.QueryAsync<ProductColor>($"SELECT {Columns} FROM {Table} c WHERE (c.is_deleted=FALSE OR c.is_deleted IS NULL) ORDER BY {DisplayOrder}", transaction: Transaction)).AsList();

    /// <summary>存在检查包含软删颜色，保持输入原文本；尾部 ASCII 空格及 ICU 比较不代表唯一索引已适配。</summary>
    public Task<bool> ExistByColorCode(string productColor)
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("Code", productColor);
        return Connection.ExecuteScalarAsync<bool>($"SELECT EXISTS (SELECT 1 FROM {Table} c WHERE rtrim(c.color_code_unique, ' ') COLLATE public.erp_dictionary_ci = rtrim(CAST(@Code AS text), ' ') COLLATE public.erp_dictionary_ci)", parameters, Transaction);
    }

    /// <summary>编码集合不修剪或去重，返回所有匹配原码且包含软删；空集合不查询。</summary>
    public async Task<IReadOnlyList<string>> GetExistingColorCodesAsync(IEnumerable<string> productColorList)
    {
        var values = productColorList.ToArray();
        if (values.Length == 0) return [];
        return (await Connection.QueryAsync<string>($"SELECT c.color_code_unique FROM {Table} c WHERE {Matches("color_code_unique", "Codes")}", Codes("Codes", values), Transaction)).AsList();
    }

    /// <summary>规范化输入后读取严格未软删的窄字段投影；仅显式更新锁要求调用方事务，空集合直接返回。</summary>
    public async Task<IReadOnlyList<ProductColor>> GetByColorCodesAsync(IEnumerable<string> colorCodes, bool withUpdLock = false)
    {
        var values = colorCodes?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (values.Length == 0) return [];
        var locking = GetLockHint(withUpdLock);
        // 写路径保持 Uid 锁顺序；无锁查询使用颜色全局显示顺序。
        var order = withUpdLock ? "c.uid" : DisplayOrder;
        return (await Connection.QueryAsync<ProductColor>($"SELECT {NarrowColumns} FROM {Table} c WHERE {Matches("color_code_unique", "Codes")} AND c.is_deleted=FALSE ORDER BY {order}{locking}", Codes("Codes", values), Transaction)).AsList();
    }

    /// <summary>检查未软删产品及中性引用并去重；保留原产品锁，中性引用只读，由父颜色锁协调新增。</summary>
    public async Task<IReadOnlyList<Guid>> GetReferencedColorIdsAsync(IEnumerable<Guid> colorIds)
    {
        var values = colorIds?.Where(x => x != Guid.Empty).Distinct().ToArray() ?? [];
        if (values.Length == 0) return [];
        var locking = GetLockHint(true);
        // PostgreSQL 不允许 DISTINCT 与 FOR UPDATE 同层，先读取并锁定实际产品行，再做内存去重。
        var rows = await Connection.QueryAsync<Guid>($"SELECT p.color_id FROM {ProductTable} p WHERE p.color_id=ANY(@Uids) AND p.is_deleted=FALSE ORDER BY p.uid{locking}", PostgreSqlParameters.Ids(values), Transaction);
        // 新增中性引用与删除入口已在父颜色行协调；检查引用只读，不再追加子行更新锁。
        var neutralRows = await Connection.QueryAsync<Guid>($"SELECT pn.color_id FROM public.product_neutral pn WHERE pn.color_id=ANY(@Uids) AND pn.is_deleted=FALSE ORDER BY pn.uid", PostgreSqlParameters.Ids(values), Transaction);
        return rows.Concat(neutralRows).Distinct().ToList();
    }

    /// <summary>分页默认五十且不增加上限；保持软删 NULL、关键词与批量筛选，普通查询不申请行锁。</summary>
    public async Task<PagedResponseBase<ProductColor>> GetProductColorPagedAsync(ProductColorSearchRequest request)
    {
        request ??= new ProductColorSearchRequest();
        var index = request.PageIndex > 0 ? request.PageIndex : 1;
        var size = request.PageSize > 0 ? request.PageSize : 50;
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (index - 1) * size);
        parameters.Add("Limit", size);
        var where = "(c.is_deleted=FALSE OR c.is_deleted IS NULL)";
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            where += " AND (c.color_code_unique COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR c.color_code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR c.color_name_en COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR c.color_name_zh COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')";
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
        }
        var batch = request.BatchKeywords?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (batch.Length > 0)
        {
            where += $" AND ({Matches("color_code_unique", "BatchKeywords")} OR {Matches("color_code", "BatchKeywords")})";
            parameters.AddTexts("BatchKeywords", batch);
        }
        var count = await Connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {Table} c WHERE {where}", parameters, Transaction);
        var rows = await Connection.QueryAsync<ProductColor>($"SELECT {Columns} FROM {Table} c WHERE {where} ORDER BY {DisplayOrder} LIMIT @Limit OFFSET @Offset", parameters, Transaction);
        return new PagedResponseBase<ProductColor> { Items = rows.AsList(), TotalCount = count, PageIndex = index, PageSize = size };
    }

    /// <summary>用存在子查询比较文本数组，避免重复输入放大返回行；列与参数名只来自固定调用。</summary>
    private static string Matches(string column, string parameter) => $"EXISTS (SELECT 1 FROM unnest(@{parameter}) AS requested(code) WHERE rtrim(c.{column}, ' ') COLLATE public.erp_dictionary_ci = rtrim(requested.code, ' ') COLLATE public.erp_dictionary_ci)";

    /// <summary>编码集合使用单个 text 数组参数，不展开为 SQL Server IN 参数。</summary>
    private static PostgreSqlParameters Codes(string name, IEnumerable<string> values)
    {
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts(name, values);
        return parameters;
    }
}
