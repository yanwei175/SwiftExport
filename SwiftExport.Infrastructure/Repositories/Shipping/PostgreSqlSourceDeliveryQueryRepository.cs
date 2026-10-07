using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>借当前会话读取来源交付净额，锁定读取只锁参与的现有来源和Posted头行；空范围与完整装箱联合协议另验。</summary>
public sealed partial class PostgreSqlSourceDeliveryQueryRepository(RepositoryBaseContext context) : ISourceDeliveryQueryRepository
{
    private static readonly EntityMetadata GrossMap = EntityMetadataCache.GetMetadata(typeof(GrossDemand));
    private static readonly EntityMetadata HeadMap = EntityMetadataCache.GetMetadata(typeof(PackingListHead));
    private static readonly EntityMetadata LineMap = EntityMetadataCache.GetMetadata(typeof(PackingListLine));

    /// <summary>仅检查原三个用户实体表与箱单三快照列；含分区表、不接受视图，不部署或证明完整历史。</summary>
    public Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken = default) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT
              EXISTS(SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname=@Schema AND t.relname=@GrossTable AND t.relkind IN ('r','p'))
              AND EXISTS(SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname=@Schema AND t.relname=@HeadTable AND t.relkind IN ('r','p'))
              AND EXISTS(SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname=@Schema AND t.relname=@LineTable AND t.relkind IN ('r','p'))
              AND (SELECT COUNT(*) FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class t ON t.oid=a.attrelid
                JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
                WHERE n.nspname=@Schema AND t.relname=@LineTable AND t.relkind IN ('r','p') AND a.attnum>0 AND NOT a.attisdropped
                  AND a.attname IN (@ModeColumn,@TypeColumn,@NumberColumn))=3;
            """, new QueryParameters([
                ("Schema",NpgsqlDbType.Text,GrossMap.SchemaName),("GrossTable",NpgsqlDbType.Text,GrossMap.TableName),
                ("HeadTable",NpgsqlDbType.Text,HeadMap.TableName),("LineTable",NpgsqlDbType.Text,LineMap.TableName),
                ("ModeColumn",NpgsqlDbType.Text,"is_non_standard_snapshot"),
                ("TypeColumn",NpgsqlDbType.Text,"source_type_snapshot"),
                ("NumberColumn",NpgsqlDbType.Text,"source_document_no_snapshot")]), cancellationToken));

    /// <summary>活动、有客户及正剩余来源只物化一次；计数/空页由SQL传输，不创建事务或预占交付量。</summary>
    public async Task<PagedResponseBase<SourceDemandDeliveryItemDto>> SearchAsync(SourceDemandDeliverySearchRequest request, CancellationToken cancellationToken = default)
    {
        var parameters = new QueryParameters([
            ("SourceType",NpgsqlDbType.Text,Clean(request.SourceType)),("CustomerId",NpgsqlDbType.Uuid,request.CustomerId),
            ("SourceNo",NpgsqlDbType.Text,Like(request.SourceNo)),("Sku",NpgsqlDbType.Text,Like(request.Sku)),
            ("Keyword",NpgsqlDbType.Text,Like(request.Keyword)),("Skip",NpgsqlDbType.Bigint,(long)(request.PageIndex-1)*request.PageSize),
            ("Take",NpgsqlDbType.Integer,request.PageSize),("Posted",NpgsqlDbType.Integer,(int)PackingListPostingStatusEnum.Posted),
            ("Active",NpgsqlDbType.Integer,(int)GrossDemandLifecycleStatusEnum.Active)]);
        var rows = (await context.Session.Connection.QueryAsync<SearchRow>(Command(SearchSql(), parameters, cancellationToken))).ToList();
        var result = new PagedResponseBase<SourceDemandDeliveryItemDto> { TotalCount = rows.First().TotalCount, PageIndex = request.PageIndex, PageSize = request.PageSize };
        foreach (var row in rows) if (row.HasSource) result.Items.Add(ToItem(row));
        return result;
    }

    /// <summary>仅按Gross身份及共同产品模式资格读取；关闭/作废、客户缺失、零负剩余原样返回，缺失或软删为null。</summary>
    public Task<SourceDemandDeliveryItemDto?> GetAsync(Guid grossDemandId, CancellationToken cancellationToken = default) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<SourceDemandDeliveryItemDto>(Command(
            Projection() + "SELECT * FROM source_delivery WHERE \"GrossDemandId\"=@GrossDemandId;",
            new QueryParameters([("GrossDemandId",NpgsqlDbType.Uuid,grossDemandId),
                ("Posted",NpgsqlDbType.Integer,(int)PackingListPostingStatusEnum.Posted),
                ("Active",NpgsqlDbType.Integer,(int)GrossDemandLifecycleStatusEnum.Active)]), cancellationToken));

    /// <summary>保留原事务门禁和输入过滤，单个命令锁实际来源/Posted头行并读取净额，不创建事务或伪造不存在的范围锁。</summary>
    public async Task<IReadOnlyList<SourceDemandDeliveryItemDto>> GetForUpdateAsync(IReadOnlyCollection<Guid> grossDemandIds, CancellationToken cancellationToken = default)
    {
        if (context.Session.Transaction is null) throw new InvalidOperationException("来源交付额度复核必须加入调用方业务事务。");
        var ids = grossDemandIds.Where(id => id != Guid.Empty).Distinct().OrderBy(id => id).ToArray();
        if (ids.Length == 0) return Array.Empty<SourceDemandDeliveryItemDto>();
        var parameters = new QueryParameters([("Ids",NpgsqlDbType.Array|NpgsqlDbType.Uuid,ids),
            ("Posted",NpgsqlDbType.Integer,(int)PackingListPostingStatusEnum.Posted),
            ("Active",NpgsqlDbType.Integer,(int)GrossDemandLifecycleStatusEnum.Active)]);
        var order = "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(i => $"get_byte(uuid_send(\"GrossDemandId\"),{i})")) + "]";
        return (await context.Session.Connection.QueryAsync<SourceDemandDeliveryItemDto>(Command(
            Projection(true) + $"SELECT * FROM source_delivery ORDER BY {order};", parameters, cancellationToken))).ToList();
    }

    /// <summary>保持原权威Posted合计、Gross快照及nullable客户；锁定聚合先物化实际行，避免对聚合结果或外连接可空侧加锁。</summary>
    internal static string Projection(bool forUpdate = false) => DeliveryProjection(forUpdate) + $"""
        , source_delivery AS {(forUpdate ? "MATERIALIZED" : "")} (
            SELECT
                g."uid" AS "GrossDemandId",
                g."version" AS "DemandVersion",
                g."source_type" AS "SourceType",
                g."source_uid" AS "SourceUid",
                g."source_no" AS "SourceNo",
                g."source_line_id" AS "SourceLineId",
                g."source_line_number" AS "SourceLineNumber",
                g."source_version" AS "SourceVersion",
                CASE WHEN c."uid" IS NOT NULL THEN g."customer_id" END AS "CustomerId",
                c."customer_code" AS "CustomerCode",
                c."customer_name" AS "CustomerName",
                g."product_id" AS "ProductId",
                g."is_non_standard" AS "IsNonStandard",
                g."sku_snapshot" AS "Sku",
                g."link_sku_snapshot" AS "LinkSku",
                g."customer_sku_snapshot" AS "CustomerSku",
                g."description_snapshot" AS "Description",
                g."color_snapshot" AS "Color",
                g."specification_snapshot" AS "Specification",
                g."image_path_snapshot" AS "ImagePath",
                g."unit_snapshot" AS "Unit",
                g."demand_qty" AS "DemandQty",
                COALESCE(d."DeliveredQty",0) AS "DeliveredQty",
                g."demand_qty"-COALESCE(d."DeliveredQty",0) AS "RemainingQty",
                g."lifecycle_status" AS "LifecycleStatus",
                (g."lifecycle_status"=@Active) AS "IsActive",
                g."required_date" AS "RequiredDate"
            FROM "public"."gross_demand" g LEFT JOIN "public"."customer" c ON c."uid"=g."customer_id" AND c."is_deleted"=FALSE
            LEFT JOIN "public"."product" p ON p."uid"=g."product_id" AND p."is_deleted"=FALSE
            LEFT JOIN delivered d ON d."GrossDemandId"=g."uid"
            WHERE g."is_deleted"=FALSE AND ((g."is_non_standard"=TRUE AND g."product_id" IS NULL)
                OR (g."is_non_standard"=FALSE AND g."product_id" IS NOT NULL AND p."uid" IS NOT NULL))
                {(forUpdate ? $"AND g.\"uid\"=ANY(@Ids) FOR UPDATE OF g" : "")}
        )
        """;

    /// <summary>普通读取保留原聚合；锁定读取只物化原范围内有效Posted头行，再对锁定事实求和。</summary>
    private static string DeliveryProjection(bool forUpdate) => forUpdate ? $"""
        WITH locked_delivery AS MATERIALIZED (
            SELECT l."gross_demand_id" AS "GrossDemandId",l."qty" AS "Qty"
            FROM "public"."packing_list_line" l JOIN "public"."packing_list_head" h ON h."uid"=l."packing_list_head_id"
            WHERE l."is_deleted"=FALSE AND h."is_deleted"=FALSE
                AND h."posting_status"=@Posted AND l."gross_demand_id"=ANY(@Ids)
            FOR UPDATE OF l,h
        ), delivered AS (
            SELECT "GrossDemandId",SUM("Qty") AS "DeliveredQty" FROM locked_delivery GROUP BY "GrossDemandId"
        )
        """ : $"""
        WITH delivered AS (
            SELECT l."gross_demand_id" AS "GrossDemandId",SUM(l."qty") AS "DeliveredQty"
            FROM "public"."packing_list_line" l JOIN "public"."packing_list_head" h ON h."uid"=l."packing_list_head_id"
            WHERE l."is_deleted"=FALSE AND h."is_deleted"=FALSE AND h."posting_status"=@Posted
            GROUP BY l."gross_demand_id"
        )
        """;

    /// <summary>固定完整过滤候选供COUNT和分页共用；HasSource区分LEFT占位，非法FETCH保持技术拒绝。</summary>
    private static string SearchSql() => Projection() + $"""
        , candidates AS MATERIALIZED (
            SELECT * FROM source_delivery s WHERE s."IsActive"=TRUE AND s."CustomerId" IS NOT NULL AND s."RemainingQty">0
              AND (@SourceType IS NULL OR rtrim(s."SourceType",' ') COLLATE public.erp_dictionary_ci=rtrim(@SourceType,' ') COLLATE public.erp_dictionary_ci)
              AND (@CustomerId IS NULL OR s."CustomerId"=@CustomerId)
              AND (@SourceNo IS NULL OR s."SourceNo" COLLATE public.erp_dictionary_ci LIKE @SourceNo ESCAPE '~')
              AND (@Sku IS NULL OR s."Sku" COLLATE public.erp_dictionary_ci LIKE @Sku ESCAPE '~')
              AND (@Keyword IS NULL OR s."SourceNo" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                OR s."Sku" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR s."LinkSku" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                OR s."CustomerSku" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR s."Description" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                OR s."Color" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')
        ), totals AS (SELECT COUNT(*)::integer AS "TotalCount" FROM candidates), page AS (
            SELECT s.*,TRUE AS "HasSource" FROM candidates s ORDER BY {Order("s")}
            LIMIT CASE WHEN @Take<=0 THEN -1 ELSE @Take END OFFSET @Skip
        )
        SELECT t."TotalCount",COALESCE(pg."HasSource",FALSE) AS "HasSource",{PageColumns}
        FROM totals t LEFT JOIN page pg ON TRUE ORDER BY {Order("pg")};
        """;

    /// <summary>页内与最终输出都沿来源字典尾空格/行号/SqlGuid顺序，不改原始文本。</summary>
    private static string Order(string alias) => $"rtrim({alias}.\"SourceNo\",' ') COLLATE public.erp_dictionary_ci ASC NULLS FIRST,rtrim({alias}.\"SourceType\",' ') COLLATE public.erp_dictionary_ci ASC NULLS FIRST,COALESCE({alias}.\"SourceLineNumber\",2147483647) ASC," +
        "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(i => $"get_byte(uuid_send({alias}.\"GrossDemandId\"),{i})")) + "] ASC NULLS FIRST";
    /// <summary>当前连接、可空父事务与原令牌贯穿一个命令，仓储不接管所有权。</summary>
    private CommandDefinition Command(string sql, object parameters, CancellationToken token) => new(sql, parameters, context.Session.Transaction, cancellationToken: token);
    /// <summary>源输入空白规则仅用于查询参数，不处理投影文本。</summary>
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    /// <summary>原四次转义次序保持，防止用户通配符扩大匹配。</summary>
    private static string? Like(string? text) => Clean(text) is { } value ? "%" + value.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%" : null;

    private const string PageColumns = """
        pg."GrossDemandId",
        pg."DemandVersion",
        pg."SourceType",
        pg."SourceUid",
        pg."SourceNo",
        pg."SourceLineId",
        pg."SourceLineNumber",
        pg."SourceVersion",
        pg."CustomerId",
        pg."CustomerCode",
        pg."CustomerName",
        pg."ProductId",
        pg."IsNonStandard",
        pg."Sku",
        pg."LinkSku",
        pg."CustomerSku",
        pg."Description",
        pg."Color",
        pg."Specification",
        pg."ImagePath",
        pg."Unit",
        pg."DemandQty",
        pg."DeliveredQty",
        pg."RemainingQty",
        pg."LifecycleStatus",
        pg."IsActive",
        pg."RequiredDate"
    """;
    /// <summary>只组装真实SQL行，必需值为NULL暴露映射错误；不在客户端重算净额、资格或页序。</summary>
    private static SourceDemandDeliveryItemDto ToItem(SearchRow row) => new()
    {
        GrossDemandId = row.GrossDemandId!.Value,
        DemandVersion = row.DemandVersion!.Value,
        SourceType = row.SourceType,
        SourceUid = row.SourceUid!.Value,
        SourceNo = row.SourceNo,
        SourceLineId = row.SourceLineId!.Value,
        SourceLineNumber = row.SourceLineNumber,
        SourceVersion = row.SourceVersion,
        CustomerId = row.CustomerId,
        CustomerCode = row.CustomerCode,
        CustomerName = row.CustomerName,
        ProductId = row.ProductId,
        IsNonStandard = row.IsNonStandard!.Value,
        Sku = row.Sku,
        LinkSku = row.LinkSku,
        CustomerSku = row.CustomerSku,
        Description = row.Description,
        Color = row.Color,
        Specification = row.Specification,
        ImagePath = row.ImagePath,
        Unit = row.Unit,
        DemandQty = row.DemandQty!.Value,
        DeliveredQty = row.DeliveredQty!.Value,
        RemainingQty = row.RemainingQty!.Value,
        LifecycleStatus = row.LifecycleStatus!.Value,
        IsActive = row.IsActive!.Value,
        RequiredDate = row.RequiredDate,
    };
    /// <summary>单结果包全部投影可空用于空页占位；HasSource是唯一占位判据，真实文本NULL保持。</summary>
    private sealed class SearchRow
    {
        public int TotalCount { get; set; }
        public bool HasSource { get; set; }
        public Guid? GrossDemandId { get; set; }
        public int? DemandVersion { get; set; }
        public string? SourceType { get; set; }
        public Guid? SourceUid { get; set; }
        public string? SourceNo { get; set; }
        public Guid? SourceLineId { get; set; }
        public int? SourceLineNumber { get; set; }
        public int? SourceVersion { get; set; }
        public Guid? CustomerId { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
        public Guid? ProductId { get; set; }
        public bool? IsNonStandard { get; set; }
        public string? Sku { get; set; }
        public string? LinkSku { get; set; }
        public string? CustomerSku { get; set; }
        public string? Description { get; set; }
        public string? Color { get; set; }
        public string? Specification { get; set; }
        public string? ImagePath { get; set; }
        public string? Unit { get; set; }
        public decimal? DemandQty { get; set; }
        public decimal? DeliveredQty { get; set; }
        public decimal? RemainingQty { get; set; }
        public GrossDemandLifecycleStatusEnum? LifecycleStatus { get; set; }
        public bool? IsActive { get; set; }
        public DateTime? RequiredDate { get; set; }
    }
    /// <summary>本切片局部显式绑定标量NULL类型，每命令独立参数，不提前建设第四方法参数框架。</summary>
    private sealed class QueryParameters((string Name, NpgsqlDbType Type, object? Value)[] parameters) : SqlMapper.IDynamicParameters
    {
        /// <summary>metadata目录及业务参数都参数化，NULL转DBNull但仍保持显式text/uuid类型。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        { foreach (var p in parameters) command.Parameters.Add(new NpgsqlParameter(p.Name, p.Type) { Value = p.Value ?? DBNull.Value }); }
    }
}
