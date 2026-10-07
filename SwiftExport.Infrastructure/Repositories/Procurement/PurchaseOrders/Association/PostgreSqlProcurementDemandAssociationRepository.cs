using System.Data;
using Dapper;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Association;

/// <summary>在既有 PostgreSQL 会话内查询采购补关联候选、执行目标并定向更新头行；不预占容量、不接管公共事务。</summary>
public sealed class PostgreSqlProcurementDemandAssociationRepository(RepositoryBaseContext context) : IProcurementDemandAssociationRepository
{
    private static readonly EntityMetadata HeadMap = EntityMetadataCache.GetMetadata(typeof(PurchaseOrderHead));
    private static readonly EntityMetadata LineMap = EntityMetadataCache.GetMetadata(typeof(PurchaseOrderLine));

    /// <summary>透传既有事务与取消令牌，不创建或关闭调用方资源。</summary>
    private CommandDefinition Command(string sql, object? args, CancellationToken ct)
        => new(sql, args, context.Session.Transaction, cancellationToken: ct);

    /// <summary>一次物化候选供总数与分页共用；未知单位原样显示，不追加提交资格或自动分配，分页值不在仓储重写。</summary>
    public async Task<PagedResponseBase<ProcurementDemandCandidateDto>> SearchCandidatesAsync(
        Guid uid, ProcurementDemandCandidateSearchRequest request, CancellationToken ct)
    {
        var keyword = request.Keyword?.Trim();
        if (!string.IsNullOrEmpty(keyword))
            keyword = "%" + keyword.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
        var parameters = new DynamicParameters();
        parameters.Add("uid", uid, DbType.Guid);
        parameters.Add("LineId", request.PurchaseOrderLineId, DbType.Guid);
        parameters.Add("SourceUid", request.SourceUid, DbType.Guid);
        parameters.Add("SourceType", string.IsNullOrWhiteSpace(request.SourceType) ? null : request.SourceType.Trim(), DbType.String);
        parameters.Add("Keyword", string.IsNullOrEmpty(keyword) ? null : keyword, DbType.String);
        parameters.Add("Skip", (long)(request.PageIndex - 1) * request.PageSize, DbType.Int64);
        parameters.Add("Take", request.PageSize, DbType.Int32);
        var rows = (await context.Session.Connection.QueryAsync<CandidateSummary, ProcurementDemandCandidateDto, CandidatePageRow>(
            Command(CandidatesSql(), parameters, ct),
            (summary, item) => new(summary.TotalCount, summary.HasItem, item), splitOn: "NetDemandId")).ToList();
        var result = new PagedResponseBase<ProcurementDemandCandidateDto>
        { TotalCount = rows.First().TotalCount, PageIndex = request.PageIndex, PageSize = request.PageSize };
        foreach (var row in rows)
        {
            // 空页占位由标记识别；真实候选映射失败直接暴露，不用零身份替代。
            if (row.HasItem) result.Items.Add(row.Item ?? throw new InvalidOperationException("采购需求候选映射缺失。"));
        }
        return result;
    }

    /// <summary>保留源去重、.NET Guid 排序和每批500身份；各批 SQL 按头/行 SqlGuid 顺序读取，不额外锁定目标。</summary>
    public async Task<IReadOnlyList<ProcurementAssociationTarget>> ReadTargetsAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken ct)
    {
        var result = new List<ProcurementAssociationTarget>();
        foreach (var ids in lineIds.Distinct().Order().Chunk(500))
            result.AddRange(await context.Session.Connection.QueryAsync<ProcurementAssociationTarget>(Command($"""
                SELECT p.uid AS "LineId",h.uid AS "HeadId",h.supplier_id AS "SupplierId",
                       h.version AS "HeadVersion",p.version AS "LineVersion",p.product_id AS "ProductId",
                       p.delivery_target_supplier_id AS "DeliveryTargetSupplierId"
                FROM public.purchase_order_line p JOIN public.purchase_order_head h ON h.uid=p.purchase_order_id
                WHERE p.uid=ANY(@Uids) AND p.is_deleted=FALSE AND h.is_deleted=FALSE AND h.lifecycle_status=0
                ORDER BY {SqlGuidOrder("h.uid")},{SqlGuidOrder("p.uid")};
                """, PostgreSqlParameters.Ids(ids), ct)));
        return result;
    }

    /// <summary>按原版本和状态先写头再按.NET Guid写行；失败立即返回，由调用方回滚公共事务，保留已经发生的审计及实体版本副作用。</summary>
    public async Task<RepositoryWriteResult> ApplyAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, CancellationToken ct)
    {
        if (context.Session.Transaction is null) throw new InvalidOperationException("采购关联写入必须加入公共事务。");
        var version = head.Version;
        context.AuditStamp.StampForUpdate(head);
        if (await context.Session.Connection.ExecuteAsync(Command($"""
            UPDATE "public"."purchase_order_head" SET "demand_source_type"=@DemandSourceType,"demand_source_uid"=@DemandSourceUid,
                "demand_source_no"=@DemandSourceNo,"version"="version"+1,
                "updated_date"=@UpdatedDate,"updated_by"=@UpdatedBy
            WHERE "uid"=@Uid AND "version"=@Version AND "is_deleted"=FALSE
                AND "lifecycle_status"=0 AND "confirmation_status" IN(0,1);
            """, PostgreSqlParameters.Entity(head, HeadMap), ct)) != 1)
            return RepositoryWriteResult.Fail(head.Uid, head.Version, "CONCURRENCY_CONFLICT", "采购版本或状态已变化，请重新预览。");
        head.Version++;
        foreach (var line in lines.OrderBy(x => x.Uid))
        {
            context.AuditStamp.StampForUpdate(line);
            if (await context.Session.Connection.ExecuteAsync(Command($"""
                UPDATE "public"."purchase_order_line" SET "unlinked_qty"=@UnlinkedQty,"source_type"=@SourceType,
                    "source_uid"=@SourceUid,"source_no"=@SourceNo,"source_line_id"=@SourceLineId,
                    "source_line_number"=@SourceLineNumber,"version"="version"+1,
                    "updated_date"=@UpdatedDate,"updated_by"=@UpdatedBy
                WHERE "uid"=@Uid AND "purchase_order_id"=@PurchaseOrderId
                    AND "version"=@Version AND "is_deleted"=FALSE;
                """, PostgreSqlParameters.Entity(line, LineMap), ct)) != 1)
                return RepositoryWriteResult.Fail(line.Uid, line.Version, "CONCURRENCY_CONFLICT", "采购行版本已变化，请重新预览。");
            line.Version++;
        }
        return RepositoryWriteResult.Ok(head.Uid, version, head.Version);
    }

    /// <summary>以物化CTE替代原临时表，计数和页只观察同一候选集合；不收紧原Link汇总或候选资格。</summary>
    private static string CandidatesSql() => $"""
        WITH candidates AS MATERIALIZED (
            SELECT n.uid AS "NetDemandId",n.version AS "Version",g.uid AS "GrossDemandId",g.version AS "SourceDemandVersion",
                   n.decomposition_revision AS "DecompositionRevision",g.source_type AS "SourceType",g.source_uid AS "SourceUid",
                   g.source_no AS "SourceNo",g.source_line_number AS "SourceLineNumber",n.product_id AS "ProductId",
                   g.is_non_standard AS "IsNonStandard",n.sku_snapshot AS "Sku",n.customer_sku_snapshot AS "CustomerSku",
                   n.description_snapshot AS "Description",n.material_type_snapshot AS "MaterialType",n.supply_type_snapshot AS "SupplyType",
                   n.unit_snapshot AS "Unit",n.delivery_target_net_demand_id AS "DeliveryTargetNetDemandId",
                   n.net_qty AS "NetQty",COALESCE(used.qty,0) AS "LinkedQty",n.net_qty-COALESCE(used.qty,0) AS "RemainingQty"
            FROM public.net_demand n JOIN public.gross_demand g ON g.uid=n.gross_demand_id
            JOIN public.purchase_order_head h ON h.uid=@uid AND h.customer_id=g.customer_id
            JOIN public.purchase_order_line p ON p.purchase_order_id=h.uid AND p.uid=@LineId
            LEFT JOIN LATERAL (SELECT SUM(l.allocated_qty-l.released_qty) AS qty FROM public.demand_execution_link l
                WHERE l.net_demand_id=n.uid AND rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
                    AND l.is_deleted=FALSE AND l.allocated_qty>l.released_qty) used ON TRUE
            WHERE n.is_deleted=FALSE AND n.lifecycle_status=0 AND g.is_deleted=FALSE AND g.lifecycle_status=0
                AND h.is_deleted=FALSE AND h.lifecycle_status=0 AND h.confirmation_status IN(0,1) AND p.is_deleted=FALSE
                AND p.unlinked_qty>0 AND n.net_qty>COALESCE(used.qty,0) AND n.decomposition_revision>0
                AND rtrim(n.source_demand_basis_fingerprint_snapshot,' ') COLLATE public.erp_dictionary_ci=rtrim(g.demand_basis_fingerprint,' ') COLLATE public.erp_dictionary_ci
                AND NULLIF(rtrim(g.demand_basis_fingerprint,' ') COLLATE public.erp_dictionary_ci,'') IS NOT NULL
                AND ((p.product_id=n.product_id AND g.is_non_standard=FALSE
                    AND rtrim(p.material_type,' ') COLLATE public.erp_dictionary_ci=rtrim(n.material_type_snapshot,' ') COLLATE public.erp_dictionary_ci
                    AND rtrim(p.supply_type,' ') COLLATE public.erp_dictionary_ci=rtrim(n.supply_type_snapshot,' ') COLLATE public.erp_dictionary_ci)
                    OR (p.product_id IS NULL AND n.product_id IS NULL AND g.is_non_standard=TRUE))
                AND rtrim(n.supply_type_snapshot,' ') COLLATE public.erp_dictionary_ci='采购'
                AND (@SourceUid IS NULL OR g.source_uid=@SourceUid)
                AND (@SourceType IS NULL OR rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci=rtrim(@SourceType,' ') COLLATE public.erp_dictionary_ci)
                AND (h.demand_source_uid IS NULL OR (g.source_uid=h.demand_source_uid
                    AND rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci=rtrim(h.demand_source_type,' ') COLLATE public.erp_dictionary_ci))
                AND (@Keyword IS NULL OR g.source_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR n.sku_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR n.customer_sku_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR n.description_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')
        ), totals AS (SELECT COUNT(*)::integer AS "TotalCount" FROM candidates), page AS (
            SELECT c.*,TRUE AS "HasItem" FROM candidates c ORDER BY {CandidateOrder("c")}
            LIMIT CASE WHEN @Take<=0 THEN -1 ELSE @Take END OFFSET @Skip
        )
        SELECT t."TotalCount",COALESCE(pg."HasItem",FALSE) AS "HasItem",
               pg."NetDemandId",pg."Version",pg."GrossDemandId",pg."SourceDemandVersion",pg."DecompositionRevision",
               pg."SourceType",pg."SourceUid",pg."SourceNo",pg."SourceLineNumber",pg."ProductId",pg."IsNonStandard",
               pg."Sku",pg."CustomerSku",pg."Description",pg."MaterialType",pg."SupplyType",pg."Unit",
               pg."DeliveryTargetNetDemandId",pg."NetQty",pg."LinkedQty",pg."RemainingQty"
        FROM totals t LEFT JOIN page pg ON TRUE ORDER BY {CandidateOrder("pg")};
        """;

    /// <summary>页内与最终结果保留SourceNo/Type的CI尾空格排序、NULL优先以及两项身份SqlGuid优先级，不改变投影文本。</summary>
    private static string CandidateOrder(string alias)
        => $"rtrim({alias}.\"SourceNo\",' ') COLLATE public.erp_dictionary_ci ASC NULLS FIRST,rtrim({alias}.\"SourceType\",' ') COLLATE public.erp_dictionary_ci ASC NULLS FIRST,{SqlGuidOrder(alias + ".\"SourceUid\"")} ASC,{alias}.\"SourceLineNumber\" ASC NULLS FIRST,{SqlGuidOrder(alias + ".\"NetDemandId\"")} ASC";



    /// <summary>保留SQL Server uniqueidentifier的字节比较顺序，不使用PG默认UUID顺序。</summary>
    private static string SqlGuidOrder(string column) => "ARRAY[" + string.Join(',',
        new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(i => $"get_byte(uuid_send({column}),{i})")) + "]";

    /// <summary>接收一次物化的总数和空页标记，供Dapper按NetDemandId分割映射原DTO。</summary>
    private sealed class CandidateSummary
    {
        public int TotalCount { get; set; }
        public bool HasItem { get; set; }
    }

    /// <summary>只在仓储内携带页行与计数；空页不把占位DTO暴露给调用方。</summary>
    private sealed class CandidatePageRow(int totalCount, bool hasItem, ProcurementDemandCandidateDto? item)
    {
        public int TotalCount { get; } = totalCount;
        public bool HasItem { get; } = hasItem;
        public ProcurementDemandCandidateDto? Item { get; } = item;
    }
}
