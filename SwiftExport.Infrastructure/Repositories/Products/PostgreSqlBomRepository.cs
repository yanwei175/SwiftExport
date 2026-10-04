using Dapper;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;
using SwiftExport.Core.Models.Products;

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
    // 仅供普通三查询判定共享资格；数值相等是临时兼容校验，不是业务选版，异常正数差异不能合并启用。
    private static readonly string OrdinaryCurrentQualification = $"""
        EXISTS (SELECT 1 FROM public.product parent WHERE {Equal("b.bom_no", "parent.bom_no")}
            AND parent.is_deleted=FALSE AND parent.bom_version>0 AND b.bom_version=parent.bom_version)
        """;
    private const string DisplayJoins = """
        LEFT JOIN public.product child ON b.component_product_id=child.uid AND child.is_deleted=FALSE
        LEFT JOIN public.product_color color ON child.color_id=color.uid AND color.is_deleted=FALSE
        """;

    /// <summary>按 Trim 后编号查询共享有效组成，每个物理行只返回一次；组件编码保留快照，展示资料取当前有效引用。</summary>
    public Task<IEnumerable<BomDto>> GetBomDtoByBomNoAsync(string bomNo) => Connection.QueryAsync<BomDto>($"""
        SELECT {DtoColumns} FROM public.bom b {DisplayJoins}
        WHERE {Equal("b.bom_no", "@BomNo")} AND b.is_deleted=FALSE AND {OrdinaryCurrentQualification}
        ORDER BY b.line_number,b.uid
        """, new { BomNo = bomNo.Trim() }, transaction: Transaction);

    /// <summary>按 Trim 后编号读取共享结构，任意未删有效引用产品提供资格；完整映射且保留存储父身份，包含停用产品。</summary>
    public Task<IEnumerable<Bom>> GetBomByBomNoAsync(string bomNo) => Connection.QueryAsync<Bom>($"""
        SELECT {EntityColumns} FROM public.bom b
        WHERE {Equal("b.bom_no", "@BomNo")} AND b.is_deleted=FALSE AND {OrdinaryCurrentQualification}
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

    /// <summary>分页展示共享有效组成，以存在性判定资格防止结果及计数放大；保留搜索与展示资料，复用事务且不新增锁。</summary>
    public async Task<PagedResponseBase<BomDto>> GetPagedAsync(BomSearchRequest request)
    {
        request ??= new BomSearchRequest();
        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 500);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        var where = new StringBuilder($"WHERE b.is_deleted=FALSE AND {OrdinaryCurrentQualification}");
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
        var from = $"FROM public.bom b {DisplayJoins}";
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

    /// <summary>按原始编号集合读取全部历史及软删组成，复用调用方事务；按存储编号精确分组，不合并数据库等值文本。</summary>
    public async Task<Dictionary<string, IEnumerable<Bom>>> GetBomsCollectionByBomNoListAsync(IEnumerable<string> bomNos)
    {
        var numbers = bomNos?.ToArray() ?? [];
        if (numbers.Length == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("BomNos", numbers);
        // EXISTS 保留集合成员语义，重复或 ICU 等值输入不会放大行；不增加版本、软删或父资格过滤。
        var rows = await Connection.QueryAsync<Bom>($"""
            SELECT {EntityColumns} FROM public.bom b
            WHERE EXISTS (SELECT 1 FROM unnest(@BomNos) input(code) WHERE {Equal("b.bom_no", "input.code")})
            ORDER BY b.uid
            """, parameters, transaction: Transaction);
        return rows.GroupBy(row => row.BomNo).ToDictionary(group => group.Key, group => group.AsEnumerable());
    }

    /// <summary>在调用方事务内以单语句读取共享事实，ICU 与尾空格规则只决定数据库组键，不按标记数值过滤组成。</summary>
    public Task<IReadOnlyList<SharedBomGroup>> GetSharedSaveFactsAsync(IReadOnlyList<string> requestedBomNos, bool withUpdLock = true)
    {
        ArgumentNullException.ThrowIfNull(requestedBomNos);
        // 暂不将 SQL Server 范围保护标记机械转换为 PG 行锁；本实现尚未接入共享写入运行链。
        var transaction = Transaction ?? throw new InvalidOperationException("共享 BOM 保存事实必须在调用方事务内读取。");
        var numbers = requestedBomNos.ToArray();
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("RequestedBomNos", numbers);
        var bomColumns = SharedFactColumns(Metadata, "b");
        var productColumns = SharedFactColumns(PostgreSqlEntityMetadata.GetMetadata(typeof(Product)), "p");
        // 输入序号来自原始数组；空数组仍读取已有产品与组成，空组不依赖 lineUid 存在。
        var sql = $"""
            WITH source AS (
                SELECT 0 AS kind,b0.uid AS entity_uid,NULL::integer AS input_index,b0.bom_no
                FROM public.bom b0 WHERE b0.is_deleted=FALSE
                UNION ALL
                SELECT 1,p0.uid,NULL::integer,p0.bom_no
                FROM public.product p0 WHERE p0.is_deleted=FALSE AND p0.bom_version>0
                    AND p0.bom_no IS NOT NULL AND btrim(p0.bom_no,' ')<>''
                UNION ALL
                SELECT 2,NULL::uuid,(input.ordinality-1)::integer,input.bom_no
                FROM unnest(@RequestedBomNos) WITH ORDINALITY input(bom_no,ordinality)
            ), ranked AS (
                SELECT kind,entity_uid,input_index,bom_no,
                    DENSE_RANK() OVER(ORDER BY rtrim(bom_no,' ') COLLATE public.erp_dictionary_ci) AS group_key
                FROM source
            )
            SELECT r.group_key AS "GroupKey",r.bom_no AS "BomNo",r.kind AS "Kind",r.input_index AS "InputIndex",
                {bomColumns},{productColumns}
            FROM ranked r
            LEFT JOIN public.bom b ON r.kind=0 AND r.entity_uid=b.uid
            LEFT JOIN public.product p ON r.kind=1 AND r.entity_uid=p.uid
            ORDER BY r.group_key,r.kind,r.entity_uid,r.input_index
            """;
        return BomSharedSaveFactsReader.ReadAsync(Connection, transaction, sql, parameters);
    }

    /// <summary>完整投影 PostgreSQL 元数据别名，Uid 排首以正确识别三类型映射的空实体。</summary>
    private static string SharedFactColumns(EntityMetadata metadata, string alias) => string.Join(",", metadata.Columns
        .OrderBy(column => column.PropertyName == "Uid" ? 0 : 1)
        .Select(column => $"{alias}.{PostgreSqlNames.Quote(column.ColumnName)} AS {PostgreSqlNames.Quote(column.PropertyName)}"));

    /// <summary>只对仓储内部固定文本表达式生成 ICU 等值条件，兼容尾部 ASCII 空格且不改变前导空格或存储快照。</summary>
    private static string Equal(string column, string value) =>
        $"rtrim({column},' ') COLLATE public.erp_dictionary_ci = rtrim({value},' ') COLLATE public.erp_dictionary_ci";
}
