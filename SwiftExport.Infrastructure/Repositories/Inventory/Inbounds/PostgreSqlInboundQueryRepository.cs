using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Receipts;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

/// <summary>借用请求会话读取入库摘要和详情，保留历史来源与分阶段观察，不接管事务或库存写入。</summary>
public sealed class PostgreSqlInboundQueryRepository(RepositoryBaseContext context) : IInboundQueryRepository
{
    /// <summary>集合内并集、类型与状态间交集在计数和分页前过滤；空集合不限，集合未传时兼容旧单值。</summary>
    public async Task<PagedResponseBase<InboundManagerListItemDto>> SearchAsync(InboundManagerSearchRequest request, CancellationToken token)
    {
        var parameters = new QueryParameters();
        // 显式 integer[] 绑定同时覆盖空选及旧单值，避免提供者猜测数组或将枚举展开成多个参数。
        parameters.Add("DocumentTypes", request.DocumentTypes?.Select(value => (int)value).ToArray()
            ?? (request.DocumentType.HasValue ? [(int)request.DocumentType.Value] : []), NpgsqlDbType.Array | NpgsqlDbType.Integer);
        parameters.Add("PostingStatuses", request.PostingStatuses?.Select(value => (int)value).ToArray()
            ?? (request.PostingStatus.HasValue ? [(int)request.PostingStatus.Value] : []), NpgsqlDbType.Array | NpgsqlDbType.Integer);
        parameters.Add("Keyword", Like(request.Keyword), NpgsqlDbType.Text);
        parameters.Add("Skip", (long)(request.PageIndex - 1) * request.PageSize, NpgsqlDbType.Bigint);
        parameters.Add("Take", request.PageSize, NpgsqlDbType.Integer);
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command($"""
            SELECT COUNT(*)::integer {SearchFilter};
            SELECT h.uid AS "Uid",h.version AS "Version",h.document_no AS "DocumentNo",h.document_type AS "DocumentType",
                h.creation_mode AS "CreationMode",h.lifecycle_status AS "LifecycleStatus",h.posting_status AS "PostingStatus",
                h.warehouse_site_id AS "WarehouseSiteId",h.warehouse_name_snapshot AS "WarehouseNameSnapshot",
                h.receipt_date AS "ReceiptDate",h.remark AS "Remark",
                (SELECT COUNT(*)::integer FROM public.inbound_document_line l WHERE l.inbound_document_id=h.uid AND l.is_deleted=FALSE) AS "LineCount",
                COALESCE((SELECT SUM(l.quantity) FROM public.inbound_document_line l WHERE l.inbound_document_id=h.uid AND l.is_deleted=FALSE),0) AS "TotalQuantity"
            {SearchFilter}
            ORDER BY h.receipt_date DESC,{SourceUuidOrder("h.uid")} DESC
            LIMIT CASE WHEN @Take<=0 THEN -1 ELSE @Take END OFFSET @Skip;
            """, parameters, token));
        // PG LIMIT 0 本可成功；负LIMIT保留源FETCH非正值的技术失败，不把非法页转换为业务默认。
        var count = await reader.ReadSingleAsync<int>();
        var rows = (await reader.ReadAsync<InboundManagerListItemDto>()).ToList();
        return new() { TotalCount = count, Items = rows, PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <summary>按有效目标头的来源集合检查整头历史，再读完整头行；无关异常不阻断缺头结果。</summary>
    public async Task<InboundManagerDetailDto?> GetDetailAsync(Guid uid, CancellationToken token)
    {
        await PostgreSqlInboundReceiptHistoryGuard.ValidateDocumentSourcesAsync(context.Session, uid, token);
        var parameters = new QueryParameters();
        parameters.Add("Uid", uid, NpgsqlDbType.Uuid);
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(DetailSql(), parameters, token));
        var header = await reader.ReadSingleOrDefaultAsync<InboundHeaderDetailDto>();
        var lines = (await reader.ReadAsync<InboundLineDetailDto>()).ToList();
        return header is null ? null : new() { Header = header, Lines = lines };
    }

    /// <summary>绑定原父事务和取消令牌，不新建或结束事务。</summary>
    private CommandDefinition Command(string sql, QueryParameters parameters, CancellationToken token) =>
        new(sql, parameters, context.Session.Transaction, cancellationToken: token);

    /// <summary>按源字面搜索顺序转义，空白不限制；不重新定义全局Unicode比较规则。</summary>
    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        "%" + value.Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";

    /// <summary>保留SQL Server uniqueidentifier尾键顺序，不能用PG uuid或.NET Guid代替。</summary>
    private static string SourceUuidOrder(string column) => "ARRAY[" + string.Join(",",
        new[] { 10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0 }.Select(offset => $"get_byte(uuid_send({column}),{offset})")) + "]";

    private const string SearchFilter = """
        FROM public.inbound_document h WHERE h.is_deleted=FALSE
          AND (cardinality(@DocumentTypes)=0 OR h.document_type=ANY(@DocumentTypes))
          AND (cardinality(@PostingStatuses)=0 OR h.posting_status=ANY(@PostingStatuses))
          AND (@Keyword IS NULL OR h.document_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
            OR h.warehouse_name_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
            OR EXISTS(SELECT 1 FROM public.inbound_document_line l WHERE l.inbound_document_id=h.uid
                AND l.is_deleted=FALSE AND l.sku_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'))
        """;

    /// <summary>详情复用整头收货事实计算净额，另以草稿 LATERAL 汇总提示；历史快照联查保留 NULL 与 DTO 默认。</summary>
    private static string DetailSql() => $"""
        SELECT uid AS "Uid",version AS "Version",document_no AS "DocumentNo",document_type AS "DocumentType",
            creation_mode AS "CreationMode",lifecycle_status AS "LifecycleStatus",posting_status AS "PostingStatus",
            warehouse_site_id AS "WarehouseSiteId",warehouse_code_snapshot AS "WarehouseCodeSnapshot",
            warehouse_name_snapshot AS "WarehouseNameSnapshot",receipt_date AS "ReceiptDate",remark AS "Remark",
            posted_date AS "PostedDate",posted_by AS "PostedBy",reversed_date AS "ReversedDate",reversed_by AS "ReversedBy",
            reverse_reason AS "ReverseReason",trigger_packing_list_id AS "TriggerPackingListId",command_id AS "CommandId",
            created_date AS "CreatedDate",created_by AS "CreatedBy",updated_date AS "UpdatedDate",updated_by AS "UpdatedBy",is_deleted AS "IsDeleted"
        FROM public.inbound_document WHERE uid=@Uid AND is_deleted=FALSE;
        WITH {PurchaseReceiptFactsSql.Ctes(PostgreSqlInboundReceiptHistoryGuard.DocumentSourceScope, includeUnprovenTypes: true)}
        SELECT l.uid AS "Uid",l.version AS "Version",l.inbound_document_id AS "InboundDocumentId",l.line_number AS "LineNumber",
            l.product_id AS "ProductId",l.sku_snapshot AS "SkuSnapshot",l.description_snapshot AS "DescriptionSnapshot",
            l.unit_snapshot AS "UnitSnapshot",l.quantity AS "Quantity",l.source_line_id AS "SourceLineId",l.original_line_id AS "OriginalLineId",
            l.created_date AS "CreatedDate",l.created_by AS "CreatedBy",l.updated_date AS "UpdatedDate",l.updated_by AS "UpdatedBy",
            p.version AS "SourceVersion",h.order_no AS "SourceNo",h.bo_no AS "BoNo",p.link_sku AS "LinkSku",product.main_image AS "ImagePath",
            p.quantity AS "OriginalQty",drafts.other_draft_count AS "OtherDraftCount",drafts.other_draft_quantity AS "OtherDraftQuantity",
            CASE WHEN p.quantity>COALESCE(received.received_qty,0) THEN p.quantity-COALESCE(received.received_qty,0) ELSE 0 END AS "RemainingReceivableQty"
        FROM public.inbound_document_line l
        INNER JOIN public.inbound_document ih ON ih.uid=l.inbound_document_id AND ih.is_deleted=FALSE
        LEFT JOIN public.purchase_order_line p ON ih.document_type=10 AND p.uid=l.source_line_id
        LEFT JOIN public.purchase_order_head h ON h.uid=p.purchase_order_id
        LEFT JOIN public.product product ON product.uid=l.product_id
        LEFT JOIN receipt_facts received ON received.source_line_id=p.uid
        LEFT JOIN LATERAL(SELECT COUNT(DISTINCT dh.uid)::integer AS other_draft_count,COALESCE(SUM(dl.quantity),0) AS other_draft_quantity
            FROM public.inbound_document_line dl JOIN public.inbound_document dh ON dh.uid=dl.inbound_document_id
            WHERE dl.source_line_id=p.uid AND dh.document_type=10 AND dh.posting_status=0 AND dh.lifecycle_status=0
              AND dl.is_deleted=FALSE AND dh.is_deleted=FALSE AND dh.uid<>@Uid) drafts ON TRUE
        WHERE l.inbound_document_id=@Uid AND l.is_deleted=FALSE ORDER BY l.line_number,{SourceUuidOrder("l.uid")};
        """;

    /// <summary>仅此查询显式绑定可空枚举和分页类型；每个命令取得独立参数实例，不修改公共参数类。</summary>
    private sealed class QueryParameters : SqlMapper.IDynamicParameters
    {
        private readonly List<NpgsqlParameter> values = [];
        /// <summary>空值保留明确数据库类型，避免提供者猜测DBNull。</summary>
        public void Add(string name, object? value, NpgsqlDbType type) => values.Add(new(name, type) { Value = value ?? DBNull.Value });
        /// <summary>将参数克隆绑定到当前命令，借用连接和事务。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        { foreach (var value in values) command.Parameters.Add(value.Clone()); }
    }
}
