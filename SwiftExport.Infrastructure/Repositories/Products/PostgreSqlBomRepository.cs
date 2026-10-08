using System.Data;
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
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Infrastructure.Repositories.Products;

/// <summary>实现 PostgreSQL BOM 仓储，按编号共享组成，正标记仅表示有效性；事务和业务编排由调用方控制。</summary>
public sealed class PostgreSqlBomRepository(RepositoryBaseContext context) : PostgreSqlDapperRepository<Bom>(context), IBomRepository
{
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string BomFactColumns = """
        b."uid" AS "Uid",
        b."version" AS "Version",
        b."is_deleted" AS "IsDeleted",
        b."created_date" AS "CreatedDate",
        b."created_by" AS "CreatedBy",
        b."updated_date" AS "UpdatedDate",
        b."updated_by" AS "UpdatedBy",
        b."bom_no" AS "BomNo",
        b."bom_version" AS "BomVersion",
        b."parent_product_id" AS "ParentProductId",
        b."parent_sku_snapshot" AS "ParentSkuSnapshot",
        b."line_number" AS "LineNumber",
        b."component_product_id" AS "ComponentProductId",
        b."component_sku_snapshot" AS "ComponentSkuSnapshot",
        b."numerator" AS "Numerator",
        b."denominator" AS "Denominator",
        b."note" AS "Note"
    """;
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string ProductFactColumns = """
        p."uid" AS "Uid",
        p."version" AS "Version",
        p."is_deleted" AS "IsDeleted",
        p."created_date" AS "CreatedDate",
        p."created_by" AS "CreatedBy",
        p."updated_date" AS "UpdatedDate",
        p."updated_by" AS "UpdatedBy",
        p."is_active" AS "IsActive",
        p."product_master_id" AS "ProductMasterId",
        p."master_sku" AS "MasterSku",
        p."sku" AS "Sku",
        p."description_en" AS "DescriptionEn",
        p."description_zh" AS "DescriptionZh",
        p."color_id" AS "ColorId",
        p."features_en" AS "FeaturesEn",
        p."features_zh" AS "FeaturesZh",
        p."material_type" AS "MaterialType",
        p."unit" AS "Unit",
        p."supply_type" AS "SupplyType",
        p."bom_version" AS "BomVersion",
        p."bom_no" AS "BomNo",
        p."note" AS "Note",
        p."link_sku" AS "LinkSku",
        p."main_image" AS "MainImage",
        p."cny_price" AS "CNYPrice",
        p."usd_price" AS "USDPrice",
        p."unit_cbm" AS "UnitCBM",
        p."unit_weight" AS "UnitWeight",
        p."default_supplier_id" AS "DefaultSupplierId",
        p."default_warehouse_site_id" AS "DefaultWarehouseSiteId",
        p."manufacturer_id" AS "ManufacturerId",
        p."manufacturer_model" AS "ManufacturerModel"
    """;

    private const string EntityColumns = """
        b."uid" AS "Uid",
        b."version" AS "Version",
        b."is_deleted" AS "IsDeleted",
        b."created_date" AS "CreatedDate",
        b."created_by" AS "CreatedBy",
        b."updated_date" AS "UpdatedDate",
        b."updated_by" AS "UpdatedBy",
        b."bom_no" AS "BomNo",
        b."bom_version" AS "BomVersion",
        b."parent_product_id" AS "ParentProductId",
        b."parent_sku_snapshot" AS "ParentSkuSnapshot",
        b."line_number" AS "LineNumber",
        b."component_product_id" AS "ComponentProductId",
        b."component_sku_snapshot" AS "ComponentSkuSnapshot",
        b."numerator" AS "Numerator",
        b."denominator" AS "Denominator",
        b."note" AS "Note"
    """;
    private const string DtoColumns = """
        b.uid AS "Uid",b.version AS "Version",b.is_deleted AS "IsDeleted",
        b.created_date AS "CreatedDate",b.created_by AS "CreatedBy",b.updated_date AS "UpdatedDate",b.updated_by AS "UpdatedBy",
        b.bom_no AS "BomNo",b.component_product_id AS "SubSkuId",b.component_sku_snapshot AS "SubSku",
        b.numerator AS "Numerator",b.denominator AS "Denominator",b.note AS "Note",
        child.description_zh AS "Description",color.color_name_zh AS "Finish",
        child.features_zh AS "Features",child.main_image AS "MainImage"
        """;
    // 标记只表示正数有效；编号由数据库归组，不按来源父或正标记数值选择组成。
    private static readonly string OrdinaryCurrentQualification = $"""
        EXISTS (SELECT 1 FROM public.product parent WHERE {Equal("b.bom_no", "parent.bom_no")}
            AND parent.is_deleted=FALSE AND parent.bom_version>0) AND b.bom_version>0
        """;
    private const string DisplayJoins = """
        LEFT JOIN public.product child ON b.component_product_id=child.uid AND child.is_deleted=FALSE
        LEFT JOIN public.product_color color ON child.color_id=color.uid AND color.is_deleted=FALSE
        """;

    /// <summary>按 Trim 后编号查询共享有效组成，每个物理行只返回一次；组件编码保留快照，展示资料取当前有效引用。</summary>
    public Task<IEnumerable<BomDto>> GetBomDtoByBomNoAsync(string bomNo) => GetBomDtoByBomNoAsync(bomNo, CancellationToken.None);

    /// <summary>按共享编号读取展示，并将页面取消令牌传入 Dapper 命令。</summary>
    public Task<IEnumerable<BomDto>> GetBomDtoByBomNoAsync(string bomNo, CancellationToken cancellationToken) => Connection.QueryAsync<BomDto>(new CommandDefinition($"""
        SELECT {DtoColumns} FROM public.bom b {DisplayJoins}
        WHERE {Equal("b.bom_no", "@BomNo")} AND b.is_deleted=FALSE AND {OrdinaryCurrentQualification}
        ORDER BY b.line_number,b.uid
        """, new { BomNo = bomNo.Trim() }, Transaction, cancellationToken: cancellationToken));

    /// <summary>按 Trim 后编号读取共享结构，任意未删有效引用产品提供资格；完整映射且保留存储父身份，包含停用产品。</summary>
    public Task<IEnumerable<Bom>> GetBomByBomNoAsync(string bomNo) => GetBomByBomNoAsync(bomNo, CancellationToken.None);

    /// <summary>按共享编号读取当前结构，支持递归树查询取消。</summary>
    public Task<IEnumerable<Bom>> GetBomByBomNoAsync(string bomNo, CancellationToken cancellationToken) => Connection.QueryAsync<Bom>(new CommandDefinition($"""
        SELECT {EntityColumns} FROM public.bom b
        WHERE {Equal("b.bom_no", "@BomNo")} AND b.is_deleted=FALSE AND {OrdinaryCurrentQualification}
        ORDER BY b.line_number,b.uid
        """, new { BomNo = bomNo.Trim() }, Transaction, cancellationToken: cancellationToken));

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

    /// <summary>判断真实产品当前绑定或候选编号是否已有有效共享组成，来源父不参与归属，孤立组成不能被候选编号激活。</summary>
    public Task<bool> HasActiveBomForProductAsync(Guid productId, string? bomNo = null) => Connection.ExecuteScalarAsync<bool>($"""
        SELECT EXISTS (SELECT 1 FROM public.bom b WHERE b.is_deleted=FALSE AND b.bom_version>0
            AND EXISTS (SELECT 1 FROM public.product parent WHERE parent.is_deleted=FALSE AND parent.bom_version>0
                AND {Equal("b.bom_no", "parent.bom_no")}
                AND ((@ProductId<>@EmptyGuid AND parent.uid=@ProductId)
                    OR (CAST(@BomNo AS text) IS NOT NULL AND {Equal("b.bom_no", "CAST(@BomNo AS text)")}))))
        """, new { ProductId = productId, EmptyGuid = Guid.Empty, BomNo = string.IsNullOrWhiteSpace(bomNo) ? null : bomNo.Trim() },
        transaction: Transaction);

    /// <summary>读取指定共享父或组件的有效组成，父和行只要求正标记；数据库存在判断避免多个共享父放大物理行。</summary>
    public async Task<IReadOnlyList<Bom>> GetCurrentReferencesForProductsAsync(IEnumerable<Guid> productIds)
    {
        var ids = productIds?.Where(uid => uid != Guid.Empty).Distinct().ToArray() ?? [];
        if (ids.Length == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.AddIds("ProductIds", ids);
        return [.. await Connection.QueryAsync<Bom>($"""
            SELECT {EntityColumns} FROM public.bom b
            WHERE b.is_deleted=FALSE AND b.bom_version>0
                AND EXISTS (SELECT 1 FROM public.product parent WHERE parent.is_deleted=FALSE AND parent.bom_version>0
                    AND {Equal("b.bom_no", "parent.bom_no")}
                    AND (parent.uid=ANY(@ProductIds) OR b.component_product_id=ANY(@ProductIds)))
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
        => GetSharedSaveFactsAsync(requestedBomNos, withUpdLock, CancellationToken.None);

    /// <summary>在共享事务中读取可取消图事实，数据库组键和现有保存锁边界保持一致。</summary>
    public Task<IReadOnlyList<SharedBomGroup>> GetSharedSaveFactsAsync(IReadOnlyList<string> requestedBomNos, bool withUpdLock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestedBomNos);
        // 共享写入的隔离级别由最外层用例控制；保留接口参数，不把旧范围提示转换为全图行锁。
        var transaction = Transaction ?? throw new InvalidOperationException("共享 BOM 保存事实必须在调用方事务内读取。");
        var numbers = requestedBomNos.ToArray();
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("RequestedBomNos", numbers);
        var bomColumns = BomFactColumns;
        var productColumns = ProductFactColumns;
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
        return ReadSharedSaveFactsAsync(Connection, transaction, sql, parameters, cancellationToken);
    }

    /// <summary>只对仓储内部固定文本表达式生成 ICU 等值条件，兼容尾部 ASCII 空格且不改变前导空格或存储快照。</summary>
    private static string Equal(string column, string value) =>
        $"rtrim({column},' ') COLLATE public.erp_dictionary_ci = rtrim({value},' ') COLLATE public.erp_dictionary_ci";

    /// <summary>按数据库本次组键汇总三类事实，输入空组和没有组成的共享产品仍保留；组键不可作为持久身份。</summary>
    private static async Task<IReadOnlyList<SharedBomGroup>> ReadSharedSaveFactsAsync(IDbConnection connection, IDbTransaction transaction,
        string sql, object parameters, CancellationToken cancellationToken)
    {
        var groups = new Dictionary<long, GroupRows>();
        await connection.QueryAsync<FactHeader, Bom, Product, int>(new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken), (header, line, product) =>
        {
            if (!groups.TryGetValue(header.GroupKey, out var group))
            {
                group = new GroupRows(header.BomNo);
                groups.Add(header.GroupKey, group);
            }
            // Kind 与首列 NULL 映射共同区分空实体；不按编号重新合并数据库等值组。
            if (header.Kind == 0 && line is not null) group.Lines.Add(line);
            if (header.Kind == 1 && product is not null) group.Products.Add(product);
            if (header.Kind == 2 && header.InputIndex.HasValue) group.InputIndexes.Add(header.InputIndex.Value);
            return 0;
        }, splitOn: "Uid,Uid");
        return groups.OrderBy(pair => pair.Key).Select(pair => new SharedBomGroup
        {
            GroupKey = pair.Key,
            BomNo = pair.Value.BomNo,
            InputIndexes = pair.Value.InputIndexes.OrderBy(index => index).ToArray(),
            Lines = pair.Value.Lines.OrderBy(line => line.Uid).ToArray(),
            Products = pair.Value.Products.OrderBy(product => product.Uid).ToArray()
        }).ToArray();
    }

    /// <summary>数据库分组头，与两个实体的 Uid 分割列分离；编号仅作原始展示文本。</summary>
    private sealed class FactHeader
    {
        public long GroupKey { get; set; }
        public string BomNo { get; set; } = null!;
        public int Kind { get; set; }
        public int? InputIndex { get; set; }
    }

    /// <summary>单次读取的模块内临时容器，三种事实分别保存，不把物理行复制成产品边。</summary>
    private sealed class GroupRows(string bomNo)
    {
        public string BomNo { get; } = bomNo;
        public List<int> InputIndexes { get; } = [];
        public List<Bom> Lines { get; } = [];
        public List<Product> Products { get; } = [];
    }
}
