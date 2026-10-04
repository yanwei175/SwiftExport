using Dapper;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Products;

/// <summary>适配 BOM 普通读取与产品范围引用查询，继承既有 PostgreSQL 基础仓储；尚不实现完整 BOM 接口或注册运行链。</summary>
public sealed class PostgreSqlBomRepository(RepositoryBaseContext context) : PostgreSqlDapperRepository<Bom>(context)
{
    private static readonly string EntityColumns = string.Join(",", Metadata.Columns.Select(column =>
        $"b.{PostgreSqlNames.Quote(column.ColumnName)} AS {PostgreSqlNames.Quote(column.PropertyName)}"));
    private const string DtoColumns = """
        b.uid AS "Uid",b.version AS "Version",b.is_deleted AS "IsDeleted",
        b.created_date AS "CreatedDate",b.created_by AS "CreatedBy",b.updated_date AS "UpdatedDate",b.updated_by AS "UpdatedBy",
        b.bom_no AS "BomNo",b.component_product_id AS "SubSkuId",b.component_sku_snapshot AS "SubSku",
        b.numerator AS "Numerator",b.denominator AS "Denominator",b.note AS "Note",
        child.description_zh AS "Description",color.color_name_zh AS "Finish",
        child.features_zh AS "Features",child.main_image AS "MainImage"
        """;
    private static readonly string CurrentParentJoin = $"""
        INNER JOIN public.product parent ON b.parent_product_id=parent.uid
            AND {Equal("b.bom_no", "parent.bom_no")} AND b.bom_version=parent.bom_version AND parent.is_deleted=FALSE
        """;
    private const string DisplayJoins = """
        LEFT JOIN public.product child ON b.component_product_id=child.uid AND child.is_deleted=FALSE
        LEFT JOIN public.product_color color ON child.color_id=color.uid AND color.is_deleted=FALSE
        """;

    /// <summary>按 Trim 后编号查询父产品当前有效组成；组件编码保留快照，中文描述、配置、颜色及图片取当前有效资料。</summary>
    public Task<IEnumerable<BomDto>> GetBomDtoByBomNoAsync(string bomNo) => Connection.QueryAsync<BomDto>($"""
        SELECT {DtoColumns} FROM public.bom b {CurrentParentJoin} {DisplayJoins}
        WHERE {Equal("b.bom_no", "@BomNo")} AND b.is_deleted=FALSE
        ORDER BY b.line_number,b.uid
        """, new { BomNo = bomNo.Trim() }, transaction: Transaction);

    /// <summary>按 Trim 后编号读取有效父产品当前结构，完整映射实体字段并按行号及身份稳定排序，包含停用产品。</summary>
    public Task<IEnumerable<Bom>> GetBomByBomNoAsync(string bomNo) => Connection.QueryAsync<Bom>($"""
        SELECT {EntityColumns} FROM public.bom b {CurrentParentJoin}
        WHERE {Equal("b.bom_no", "@BomNo")} AND b.is_deleted=FALSE
        ORDER BY b.line_number,b.uid
        """, new { BomNo = bomNo.Trim() }, transaction: Transaction);

    /// <summary>按编号及指定版本读取未软删行，不新增父产品当前版本或有效性联接；空白编号及非正版本直接返回空集合。</summary>
    public async Task<IReadOnlyList<Bom>> GetByBomNoAndVersionAsync(string bomNo, int version)
    {
        if (string.IsNullOrWhiteSpace(bomNo) || version <= 0) return [];
        return [.. await Connection.QueryAsync<Bom>($"""
            SELECT {EntityColumns} FROM public.bom b
            WHERE {Equal("b.bom_no", "@BomNo")} AND b.bom_version=@BomVersion AND b.is_deleted=FALSE
            ORDER BY b.line_number,b.uid
            """, new { BomNo = bomNo.Trim(), BomVersion = version }, transaction: Transaction)];
    }

    /// <summary>判断任意版本的有效组成是否匹配父身份或编号；空 Guid 不参与身份条件，空白编号转 NULL，不校验父资格。</summary>
    public Task<bool> HasActiveBomForProductAsync(Guid productId, string? bomNo = null) => Connection.ExecuteScalarAsync<bool>($"""
        SELECT EXISTS (SELECT 1 FROM public.bom b WHERE b.is_deleted=FALSE
            AND ((@ProductId<>@EmptyGuid AND b.parent_product_id=@ProductId)
                OR (CAST(@BomNo AS text) IS NOT NULL AND {Equal("b.bom_no", "CAST(@BomNo AS text)")})))
        """, new { ProductId = productId, EmptyGuid = Guid.Empty, BomNo = string.IsNullOrWhiteSpace(bomNo) ? null : bomNo.Trim() },
        transaction: Transaction);

    /// <summary>读取指定产品作为父或子的当前未删组成，使用数据库编号等值规则联接有效父；复用事务且不增加锁，空输入不查询。</summary>
    public async Task<IReadOnlyList<Bom>> GetCurrentReferencesForProductsAsync(IEnumerable<Guid> productIds)
    {
        var ids = productIds?.Where(uid => uid != Guid.Empty).Distinct().ToArray() ?? [];
        if (ids.Length == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.AddIds("ProductIds", ids);
        return [.. await Connection.QueryAsync<Bom>($"""
            SELECT {EntityColumns} FROM public.bom b {CurrentParentJoin}
            WHERE b.is_deleted=FALSE AND (b.parent_product_id=ANY(@ProductIds) OR b.component_product_id=ANY(@ProductIds))
            ORDER BY b.uid
            """, parameters, transaction: Transaction)];
    }

    /// <summary>按原始编号及组件编码快照判断是否存在，不修剪输入或过滤软删、版本及父产品事实。</summary>
    public Task<bool> IsBomItemExistAsync(string bomNo, string productSku) => Connection.ExecuteScalarAsync<bool>($"""
        SELECT EXISTS (SELECT 1 FROM public.bom b
            WHERE {Equal("b.bom_no", "@BomNo")} AND {Equal("b.component_sku_snapshot", "@SubSku")})
        """, new { BomNo = bomNo, SubSku = productSku }, transaction: Transaction);

    /// <summary>分页展示当前有效组成，保留十字段关键词、三字段批量编码以及中文当前资料；复用调用方事务，不新增锁或隔离。</summary>
    public async Task<PagedResponseBase<BomDto>> GetPagedAsync(BomSearchRequest request)
    {
        request ??= new BomSearchRequest();
        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 500);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        var where = new StringBuilder("WHERE b.is_deleted=FALSE");
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            // 只组合固定列名；空格、问号和字面通配符继续由公共模式生成器处理。
            var columns = new[] { "b.bom_no", "b.parent_sku_snapshot", "b.component_sku_snapshot", "child.description_en",
                "child.description_zh", "child.features_en", "child.features_zh", "color.color_code", "color.color_name_en", "color.color_name_zh" };
            where.Append(" AND (").Append(string.Join(" OR ", columns.Select(column =>
                $"{column} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'"))).Append(')');
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
        }
        var batchKeywords = request.BatchKeywords?.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(300).ToArray() ?? [];
        if (batchKeywords.Length > 0)
        {
            parameters.AddTexts("BatchKeywords", batchKeywords);
            // EXISTS 保留集合成员语义，重复或 ICU 等价输入不会放大组成行及总数。
            where.Append($" AND EXISTS (SELECT 1 FROM unnest(@BatchKeywords) input(code) WHERE {Equal("b.bom_no", "input.code")}")
                .Append($" OR {Equal("b.parent_sku_snapshot", "input.code")} OR {Equal("b.component_sku_snapshot", "input.code")})");
        }
        var from = $"FROM public.bom b {CurrentParentJoin} {DisplayJoins}";
        // 原 NOLOCK 不转换成额外业务锁；计数与取页复用原有两结果集及事务边界。
        using var multi = await Connection.QueryMultipleAsync($"""
            SELECT COUNT(1) {from} {where};
            SELECT {DtoColumns} {from} {where}
            ORDER BY rtrim(b.bom_no,' ') COLLATE public.erp_dictionary_ci,b.line_number,b.uid
            LIMIT @PageSize OFFSET @Offset
            """, parameters, transaction: Transaction);
        var totalCount = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<BomDto>();
        return new PagedResponseBase<BomDto> { Items = items.ToList(), TotalCount = totalCount, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>只对仓储内部固定文本表达式生成 ICU 等值条件，兼容尾部 ASCII 空格且不改变前导空格或存储快照。</summary>
    private static string Equal(string column, string value) =>
        $"rtrim({column},' ') COLLATE public.erp_dictionary_ci = rtrim({value},' ') COLLATE public.erp_dictionary_ci";
}
