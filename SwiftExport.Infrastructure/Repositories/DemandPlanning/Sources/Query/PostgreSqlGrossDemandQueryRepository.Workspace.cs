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
    /// <summary>只返回未删除需求使用的有效客户；不依赖客户管理权限，不读取或汇总任何下游表。</summary>
    public async Task<List<GrossDemandCustomerOptionDto>> GetCustomerOptionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows=await context.Session.Connection.QueryAsync<GrossDemandCustomerOptionDto>(new CommandDefinition("""
            SELECT c.uid AS "Id",c.customer_code AS "Code",c.customer_name AS "Name"
            FROM public.customer c
            WHERE c.is_deleted=FALSE AND c.uid<>'00000000-0000-0000-0000-000000000000'::uuid
                AND EXISTS(SELECT 1 FROM public.gross_demand d WHERE d.customer_id=c.uid AND d.is_deleted=FALSE)
            ORDER BY c.customer_code COLLATE public.erp_dictionary_ci NULLS FIRST,
                     c.customer_name COLLATE public.erp_dictionary_ci NULLS FIRST,c.uid
            """,transaction:context.Session.Transaction,cancellationToken:cancellationToken));
        return rows.AsList();
    }

    /// <summary>先读取筛选列表，再独立计数；指定来源时返回全部行，借用可空外层事务并将取消与数据库错误原样传播。</summary>
    public async Task<PagedResponseBase<GrossDemandListItemDto>> SearchWorkspaceAsync(
        GrossDemandSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var sourceMode = request.SourceUid.HasValue;
        var pageIndex = sourceMode ? 1 : Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var (where, parameters) = BuildWorkspaceFilters(request);
        if (!sourceMode)
        {
            parameters.Scalars.Add("Offset", (long)(pageIndex - 1) * pageSize);
            parameters.Scalars.Add("PageSize", pageSize);
        }
        var whereSql = string.Join(" AND ", where);
        var pagingSql = sourceMode ? ";" : "LIMIT @PageSize OFFSET @Offset;";
        // 只投影成品需求本体快照，下游执行事实由独立订单跟踪读取。
        var selectSql = $$"""
            SELECT d.uid AS "Uid",d.version AS "Version",d.lifecycle_status AS "LifecycleStatus",
                   d.source_type AS "SourceType",d.source_uid AS "SourceUid",d.source_no AS "SourceNo",
                   d.source_line_id AS "SourceLineId",d.source_line_number AS "SourceLineNumber",d.source_version AS "SourceVersion",
                   d.customer_id AS "CustomerId",d.product_id AS "ProductId",d.is_non_standard AS "IsNonStandard",
                   d.sku_snapshot AS "Sku",d.link_sku_snapshot AS "LinkSku",d.customer_sku_snapshot AS "CustomerSku",
                   d.description_snapshot AS "Description",d.color_snapshot AS "Color",d.specification_snapshot AS "Specification",
                   d.image_path_snapshot AS "ImagePath",d.unit_snapshot AS "Unit",d.demand_qty AS "DemandQty",
                   d.required_date AS "RequiredDate",d.remark AS "Remark",
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

    /// <summary>在两种加载模式中应用相同本体条件；客户按身份精确多选，关键词按普通文字包含匹配。</summary>
    private static (List<string> Where, WorkspaceParameters Parameters) BuildWorkspaceFilters(GrossDemandSearchRequest request)
    {
        var where = new List<string> { "d.is_deleted=FALSE" };
        var parameters = new WorkspaceParameters();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            where.Add("(d.source_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR d.sku_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR d.link_sku_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR d.customer_sku_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR d.description_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')");
            parameters.Scalars.Add("Keyword", "%" + request.Keyword.Trim().Replace("~","~~").Replace("%","~%").Replace("_","~_") + "%");
        }
        parameters.CustomerIds = request.CustomerIds?.Distinct().ToArray() ?? [];
        if (parameters.CustomerIds.Length != 0)
        {
            where.Add("d.customer_id=ANY(@CustomerIds)");
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
            parameters.Scalars.AddTimestamp("RequiredDateFrom", request.RequiredDateFrom.Value.Date);
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
        public Guid[] CustomerIds { get; set; } = [];

        /// <summary>为列表和计数各建独立参数；保留未知枚举值，避免 Dapper 展开或驱动猜测 integer[]。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            Scalars.AddParameters(command, identity);
            if (LifecycleStatuses.Length != 0)
                command.Parameters.Add(new NpgsqlParameter("LifecycleStatuses", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = LifecycleStatuses });
            if (CustomerIds.Length != 0)
                command.Parameters.Add(new NpgsqlParameter("CustomerIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = CustomerIds });
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
