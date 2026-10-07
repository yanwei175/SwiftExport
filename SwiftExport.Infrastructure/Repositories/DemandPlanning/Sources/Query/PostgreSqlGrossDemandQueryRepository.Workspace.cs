using Dapper;
using System.Data;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;

/// <summary>读取来源工作台列表、导航分组和单条本体详情；保留源筛选、分页、聚合及快照。</summary>
public sealed partial class PostgreSqlGrossDemandQueryRepository
{
    /// <summary>先读取筛选列表，再独立计数；指定来源时返回全部行，借用可空外层事务并将取消与数据库错误原样传播。</summary>
    public async Task<PagedResponseBase<GrossDemandListItemDto>> SearchWorkspaceAsync(
        GrossDemandSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourceMode = request.SourceUid.HasValue;
        var pageIndex = sourceMode ? 1 : Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var (where, parameters) = BuildWorkspaceFilters(request);
        if (!sourceMode)
        {
            parameters.Scalars.Add("Offset", (pageIndex - 1) * pageSize);
            parameters.Scalars.Add("PageSize", pageSize);
        }
        var whereSql = string.Join(" AND ", where);
        var pagingSql = sourceMode ? ";" : "LIMIT @PageSize OFFSET @Offset;";
        // 三项提示只判断原资格的存在性，不套用 Trace 更严格的单位匹配，也不汇总或展开多对多关联。
        var selectSql = $$"""
            SELECT d.uid AS "Uid",d.version AS "Version",d.lifecycle_status AS "LifecycleStatus",
                   d.source_type AS "SourceType",d.source_uid AS "SourceUid",d.source_no AS "SourceNo",
                   d.source_line_id AS "SourceLineId",d.source_line_number AS "SourceLineNumber",d.source_version AS "SourceVersion",
                   d.customer_id AS "CustomerId",d.product_id AS "ProductId",d.is_non_standard AS "IsNonStandard",
                   d.sku_snapshot AS "Sku",d.link_sku_snapshot AS "LinkSku",d.customer_sku_snapshot AS "CustomerSku",
                   d.description_snapshot AS "Description",d.color_snapshot AS "Color",d.specification_snapshot AS "Specification",
                   d.image_path_snapshot AS "ImagePath",d.unit_snapshot AS "Unit",d.demand_qty AS "DemandQty",
                   d.required_date AS "RequiredDate",d.remark AS "Remark",
                   {{WorkspaceActiveMaterialExists}} AS "HasActiveMaterial",
                   {{WorkspaceActivePurchaseExists}} AS "HasValidPurchase",
                   {{WorkspacePostedDeliveryExists}} AS "HasPostedDelivery",
                   d.created_date AS "CreatedDate",d.created_by AS "CreatedBy",d.updated_date AS "UpdatedDate",d.updated_by AS "UpdatedBy"
            FROM public.gross_demand d
            WHERE {{whereSql}}
            ORDER BY d.required_date ASC NULLS LAST,d.created_date ASC NULLS FIRST,
                     {{WorkspaceSqlGuidOrder("d.uid")}} ASC
            {{pagingSql}}
            """;
        var countSql = $"SELECT COUNT(1)::integer FROM public.gross_demand d WHERE {whereSql};";
        // 不合并结果集或创建快照：源列表与总数在两个命令阶段观察数据库变化。
        var items = await context.Session.Connection.QueryAsync<GrossDemandListItemDto>(new CommandDefinition(
            selectSql, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        var total = await context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            countSql, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<GrossDemandListItemDto>
        {
            Items = [.. items],
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = sourceMode ? Math.Max(1, total) : pageSize
        };
    }

    // 只判断活动物料存在，不追加版本、指纹或客户资格。
    private const string WorkspaceActiveMaterialExists = "EXISTS (SELECT 1 FROM public.net_demand n WHERE n.gross_demand_id=d.uid AND n.is_deleted=FALSE AND n.lifecycle_status=0)";
    // 保留原采购资格：版本/指纹/客户/目标/非空单位；不增加来源单位等值、采购确认状态或入库历史 Guard。
    private const string WorkspaceActivePurchaseExists = """
        EXISTS (SELECT 1 FROM public.net_demand n
            JOIN public.demand_execution_link l ON l.net_demand_id=n.uid AND l.is_deleted=FALSE AND l.link_status=0
                AND rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO' AND l.allocated_qty>l.released_qty
            JOIN public.purchase_order_line pol ON pol.uid=l.target_line_id AND pol.purchase_order_id=l.target_head_id AND pol.is_deleted=FALSE
            JOIN public.purchase_order_head poh ON poh.uid=l.target_head_id AND poh.is_deleted=FALSE AND poh.lifecycle_status<>9
            WHERE n.gross_demand_id=d.uid AND n.is_deleted=FALSE AND n.lifecycle_status=0
                AND n.source_demand_version_snapshot=d.version
                AND NULLIF(rtrim(d.demand_basis_fingerprint,' ') COLLATE public.erp_dictionary_ci,'') IS NOT NULL
                AND rtrim(n.source_demand_basis_fingerprint_snapshot,' ') COLLATE public.erp_dictionary_ci=rtrim(d.demand_basis_fingerprint,' ') COLLATE public.erp_dictionary_ci
                AND ((rtrim(d.source_type,' ') COLLATE public.erp_dictionary_ci='MD' AND d.source_version IS NULL AND n.source_version_snapshot IS NULL)
                    OR (d.source_version IS NOT NULL AND n.source_version_snapshot=d.source_version))
                AND d.customer_id IS NOT NULL AND poh.customer_id=d.customer_id
                AND NULLIF(btrim(pol.unit_snapshot,' ') COLLATE public.erp_dictionary_ci,'') IS NOT NULL)
        """;
    // 原交付提示只看未删除已过账装箱头行，不追加 Trace 的历史完整性校验。
    private const string WorkspacePostedDeliveryExists = """
        EXISTS (SELECT 1 FROM public.packing_list_line pl JOIN public.packing_list_head ph ON ph.uid=pl.packing_list_head_id AND ph.is_deleted=FALSE AND ph.posting_status=1 WHERE pl.gross_demand_id=d.uid AND pl.is_deleted=FALSE)
    """;

    /// <summary>保留每项原筛选输入规则和 TraceView 精确分支；集合去重不删空值，不因来源模式跳过其他筛选。</summary>
    private static (List<string> Where, WorkspaceParameters Parameters) BuildWorkspaceFilters(GrossDemandSearchRequest request)
    {
        var where = new List<string> { "d.is_deleted=FALSE" };
        var parameters = new WorkspaceParameters();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            where.Add("(d.source_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '' OR d.sku_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '' OR d.customer_sku_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '' OR d.description_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '')");
            parameters.Scalars.Add("Keyword", $"%{request.Keyword.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(request.CustomerKeyword))
        {
            where.Add("""
                EXISTS (SELECT 1 FROM public.customer c WHERE c.uid=d.customer_id AND c.is_deleted=FALSE AND (c.customer_code COLLATE public.erp_dictionary_ci LIKE @CustomerKeyword ESCAPE '~' OR c.customer_name COLLATE public.erp_dictionary_ci LIKE @CustomerKeyword ESCAPE '~'))
            """);
            parameters.Scalars.Add("CustomerKeyword", SqlLikePattern.Build(request.CustomerKeyword));
        }
        // 精确识别既有六个快捷条件，未知/null/All 均不加条件，不擅自 Trim 或改大小写。
        switch (request.TraceView)
        {
            case "NoMaterial": where.Add("NOT " + WorkspaceActiveMaterialExists); break;
            case "HasMaterial": where.Add(WorkspaceActiveMaterialExists); break;
            case "NoPurchase": where.Add("NOT " + WorkspaceActivePurchaseExists); break;
            case "HasPurchase": where.Add(WorkspaceActivePurchaseExists); break;
            case "NoDelivery": where.Add("NOT " + WorkspacePostedDeliveryExists); break;
            case "HasDelivery": where.Add(WorkspacePostedDeliveryExists); break;
        }
        AddWorkspaceTextFilter(request.SourceTypes, "d.source_type", "SourceTypes", where, parameters.Scalars);
        AddWorkspaceTextFilter(request.SourceNos, "d.source_no", "SourceNos", where, parameters.Scalars);
        AddWorkspaceTextFilter(request.Skus, "d.sku_snapshot", "Skus", where, parameters.Scalars);
        AddWorkspaceTextFilter(request.CustomerSkus, "d.customer_sku_snapshot", "CustomerSkus", where, parameters.Scalars);
        parameters.LifecycleStatuses = request.LifecycleStatuses?.Distinct().Select(value => (int)value).ToArray() ?? [];
        if (parameters.LifecycleStatuses.Length != 0)
            where.Add("d.lifecycle_status=ANY(CAST(@LifecycleStatuses AS integer[]))");
        if (request.RequiredDateFrom.HasValue)
        {
            where.Add("d.required_date>=@RequiredDateFrom");
            parameters.Scalars.AddTimestamp("RequiredDateFrom", request.RequiredDateFrom.Value);
        }
        if (request.RequiredDateTo.HasValue)
        {
            where.Add("d.required_date<@RequiredDateTo + INTERVAL '1 day'");
            parameters.Scalars.AddTimestamp("RequiredDateTo", request.RequiredDateTo.Value.Date);
        }
        if (request.IsNonStandard.HasValue)
        {
            where.Add("d.is_non_standard=@IsNonStandard");
            parameters.Scalars.Add("IsNonStandard", request.IsNonStandard.Value);
        }
        if (request.SourceUid.HasValue)
        {
            where.Add("d.source_uid=@SourceUid");
            parameters.Scalars.Add("SourceUid", request.SourceUid.Value);
        }
        return (where, parameters);
    }

    /// <summary>将原 Distinct 文本集合绑定为 text[]，SQL 比较保留 CI 与 ASCII 尾空格等值；不修剪输入或投影文本。</summary>
    private static void AddWorkspaceTextFilter(IEnumerable<string>? values, string column, string name,
        ICollection<string> where, PostgreSqlParameters parameters)
    {
        var array = values?.Distinct().ToArray() ?? [];
        if (array.Length == 0) return;
        where.Add($"rtrim({column},' ') COLLATE public.erp_dictionary_ci IN (SELECT rtrim(value,' ') COLLATE public.erp_dictionary_ci FROM unnest(CAST(@{name} AS text[])) AS filter(value))");
        parameters.AddTexts(name, array);
    }

    /// <summary>同日期下使用 SQL Server uniqueidentifier 字节优先级，避免 PostgreSQL 默认 UUID 排序改变分页。</summary>
    private static string WorkspaceSqlGuidOrder(string column) => "ARRAY[" + string.Join(',',
        new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(i => $"get_byte(uuid_send({column}),{i})")) + "]";

    /// <summary>仅组合本列表所需的共享标量/文本数组参数和明确 integer[] 状态，不修改共享参数类或旧分组参数。</summary>
    private sealed class WorkspaceParameters : SqlMapper.IDynamicParameters
    {
        public PostgreSqlParameters Scalars { get; } = new();
        public int[] LifecycleStatuses { get; set; } = [];

        /// <summary>为列表和计数各建独立参数；保留未知枚举值，避免 Dapper 展开或驱动猜测 integer[]。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            Scalars.AddParameters(command, identity);
            if (LifecycleStatuses.Length != 0)
                command.Parameters.Add(new NpgsqlParameter("LifecycleStatuses", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = LifecycleStatuses });
        }
    }

    /// <summary>仅按软删除与调用方生命周期筛选，以SourceUid独立聚合两项MAX及数量，不选最新来源或校验客户资格。</summary>
    public async Task<IReadOnlyList<GrossDemandSourceGroupDto>> SearchSourceGroupsAsync(
        GrossDemandSourceGroupSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var statuses = request.LifecycleStatuses?.Distinct().Select(value => (int)value).ToArray() ?? [];
        SqlMapper.IDynamicParameters parameters = new PostgreSqlParameters();
        var statusFilter = string.Empty;
        if (statuses.Length != 0)
        {
            // 既有Add仅设置Value，驱动类型仍Unknown；此切片明确integer[]，不改共享参数类或依赖包装器名称。
            parameters = new SourceLifecycleParameters(statuses);
            statusFilter = " AND d.lifecycle_status=ANY(CAST(@SourceLifecycleStatuses AS integer[]))";
        }
        // MAX返回原字段值；只对排序键补源等值尾空格语义，不修剪展示文本或改用.NET Ordinal。
        // SourceUid尾键采用已冻结SQL Server规范UUID字节顺序，不使用PG默认UUID顺序。
        var sql = $$"""
            SELECT d.source_uid AS "SourceUid",
                   MAX(d.source_type COLLATE public.erp_dictionary_ci) AS "SourceType",
                   MAX(d.source_no COLLATE public.erp_dictionary_ci) AS "SourceNo",
                   COUNT(1)::integer AS "DemandCount"
            FROM public.gross_demand d
            WHERE d.is_deleted=FALSE{{statusFilter}}
            GROUP BY d.source_uid
            ORDER BY rtrim(MAX(d.source_type COLLATE public.erp_dictionary_ci),' ') COLLATE public.erp_dictionary_ci NULLS FIRST,
                     rtrim(MAX(d.source_no COLLATE public.erp_dictionary_ci),' ') COLLATE public.erp_dictionary_ci NULLS FIRST,
                     ARRAY[get_byte(uuid_send(d.source_uid),10),get_byte(uuid_send(d.source_uid),11),
                           get_byte(uuid_send(d.source_uid),12),get_byte(uuid_send(d.source_uid),13),
                           get_byte(uuid_send(d.source_uid),14),get_byte(uuid_send(d.source_uid),15),
                           get_byte(uuid_send(d.source_uid),8),get_byte(uuid_send(d.source_uid),9),
                           get_byte(uuid_send(d.source_uid),7),get_byte(uuid_send(d.source_uid),6),
                           get_byte(uuid_send(d.source_uid),5),get_byte(uuid_send(d.source_uid),4),
                           get_byte(uuid_send(d.source_uid),3),get_byte(uuid_send(d.source_uid),2),
                           get_byte(uuid_send(d.source_uid),1),get_byte(uuid_send(d.source_uid),0)]
            """;
        return (await context.Session.Connection.QueryAsync<GrossDemandSourceGroupDto>(new CommandDefinition(
            sql, parameters, context.Session.Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>读取指定未删除来源本体，NULL/单位原文/版本/枚举及审计墙上时间原样投影；借用可空外层事务。</summary>
    public Task<GrossDemandDetailDto?> GetWorkspaceDetailAsync(Guid uid, CancellationToken cancellationToken = default)
        => context.Session.Connection.QuerySingleOrDefaultAsync<GrossDemandDetailDto>(new CommandDefinition(
            WorkspaceDetailSql, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken));

    // 只读本体，既有CLR别名直接对应DTO；不加入客户/产品资格、联表集合、指纹检查或业务数量重算。
    private const string WorkspaceDetailSql = """
        SELECT d.uid AS "Uid",d.version AS "Version",d.lifecycle_status AS "LifecycleStatus",
               d.source_type AS "SourceType",d.source_uid AS "SourceUid",d.source_no AS "SourceNo",
               d.source_line_id AS "SourceLineId",d.source_line_number AS "SourceLineNumber",d.source_version AS "SourceVersion",
               d.customer_id AS "CustomerId",d.product_id AS "ProductId",d.is_non_standard AS "IsNonStandard",
               d.sku_snapshot AS "Sku",d.link_sku_snapshot AS "LinkSku",d.customer_sku_snapshot AS "CustomerSku",
               d.description_snapshot AS "Description",d.color_snapshot AS "Color",d.specification_snapshot AS "Specification",
               d.image_path_snapshot AS "ImagePath",d.unit_snapshot AS "Unit",d.demand_qty AS "DemandQty",
               d.required_date AS "RequiredDate",d.remark AS "Remark",
               d.lifecycle_changed_at AS "LifecycleChangedAt",d.lifecycle_changed_by AS "LifecycleChangedBy",
               d.lifecycle_changed_reason AS "LifecycleChangedReason",d.created_date AS "CreatedDate",d.created_by AS "CreatedBy",
               d.updated_date AS "UpdatedDate",d.updated_by AS "UpdatedBy"
        FROM public.gross_demand d
        WHERE d.uid=@Uid AND d.is_deleted=FALSE
        """;

    /// <summary>仅绑定本查询的生命周期integer[]；每次命令创建独立参数，不构造通用数组框架。</summary>
    private sealed class SourceLifecycleParameters(int[] values) : SqlMapper.IDynamicParameters
    {
        /// <summary>将已去重状态作为一个明确integer[]参数传递，避免Dapper展开或驱动猜测枚举数组。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
            => command.Parameters.Add(new NpgsqlParameter("SourceLifecycleStatuses", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = values });
    }
}
