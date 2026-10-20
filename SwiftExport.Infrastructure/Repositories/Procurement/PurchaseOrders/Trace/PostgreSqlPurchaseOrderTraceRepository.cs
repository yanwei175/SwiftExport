using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Trace;

/// <summary>读取采购全部历史证据；起始历史行范围仅供整入库头门禁，后续六结果独立查询。</summary>
public sealed class PostgreSqlPurchaseOrderTraceRepository(RepositoryBaseContext context) : IPurchaseOrderTraceRepository
{

    /// <summary>同会话借用可空事务，一次scope、ScopedGuard及六结果；空头早返释放，取消和技术异常原样上交。</summary>
    public async Task<PurchaseOrderTraceDto?> GetAsync(Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters = new PostgreSqlParameters();
        parameters.Add("PurchaseOrderId", purchaseOrderId);
        parameters.Add("PurchaseReceipt", (int)InboundDocumentTypeEnum.PurchaseReceipt);
        parameters.Add("FormalPostingStatuses", new[] { (int)InboundPostingStatusEnum.Posted, (int)InboundPostingStatusEnum.Reversed });
        // 已删采购行也定位整入库头；scope仅供门禁，不冻结后续六阶段证据。
        var scope = (await context.Session.Connection.QueryAsync<Guid>(new CommandDefinition(
            $"SELECT scopeLine.\"uid\" AS \"PurchaseLineId\" FROM \"public\".\"purchase_order_line\" scopeLine WHERE scopeLine.\"purchase_order_id\"=@PurchaseOrderId;",
            new TraceParameters(parameters), context.Session.Transaction, cancellationToken: cancellationToken))).ToList();
        await PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(context.Session, scope, cancellationToken);
        using var grid = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            TraceSql(), new TraceParameters(parameters), context.Session.Transaction, cancellationToken: cancellationToken));
        var trace = await grid.ReadSingleOrDefaultAsync<PurchaseOrderTraceDto>();
        if (trace is null) return null;

        var lines = (await grid.ReadAsync<PurchaseOrderTraceLineDto>()).ToList();
        var links = (await grid.ReadAsync<PurchaseOrderTraceLinkDto>()).ToList();
        var reports = (await grid.ReadAsync<PurchaseOrderTraceProgressDto>()).ToList();
        var receipts = (await grid.ReadAsync<PurchaseOrderTraceReceiptDto>()).ToList();
        trace.Operations = (await grid.ReadAsync<PurchaseOrderTraceOperationDto>()).ToList();

        foreach (var line in lines)
        {
            line.Links = links.Where(x => x.PurchaseOrderLineId == line.PurchaseOrderLineId).ToList();
            line.ProgressHistory = reports.Where(x => x.PurchaseOrderLineId == line.PurchaseOrderLineId).ToList();
            line.Receipts = receipts.Where(x => x.PurchaseOrderLineId == line.PurchaseOrderLineId).ToList();
            // OriginKind 只记录创建类别；独立采购可后续补关联，按实际 Link 核实来源证据。
            if (trace.OriginKind != 2 && line.Links.Count == 0 && !line.IsDeleted)
                line.ReviewReason = "当前采购行未找到需求 Link，请核实来源。";
            else if (line.Links.Any(x => x.NeedsReview))
                line.ReviewReason = "部分需求来源身份、版本、指纹或贡献数量需核实。";
            // 原.NET空白和逐字符比较不能换成SQL字典规则；不一致保留未知收货量NULL。
            if (line.Receipts.Any(x =>
                (string.IsNullOrWhiteSpace(x.UnitSnapshot) || x.UnitSnapshot != line.UnitSnapshot)))
                line.ReviewReason = "正式入库单位与采购行不一致，净收货量需核实。";
            else
                line.FormalReceivedQty = line.Receipts.Sum(x => x.NetQuantity);
        }
        trace.Lines = lines;
        return trace;
    }

    /// <summary>独立读取当前历史集合；整行最新反馈不由稍后History覆盖，理由CASE及SQL三值逻辑沿源顺序。</summary>
    private static string TraceSql() => $"""
        SELECT h."uid" AS "PurchaseOrderId",
                       h."order_no" AS "OrderNo",
                       h."supplier_name" AS "SupplierName",
                       h."origin_kind" AS "OriginKind",
                       h."confirmation_status" AS "ConfirmationStatus",
                       h."lifecycle_status" AS "LifecycleStatus"
                FROM "public"."purchase_order_head" h
                WHERE h."uid"=@PurchaseOrderId AND COALESCE(h."is_deleted",FALSE)=FALSE;

        SELECT l."uid" AS "PurchaseOrderLineId",
                       l."line_number" AS "LineNumber",
                       l."is_deleted" AS "IsDeleted",
                       l."sku" AS "Sku",
                       l."unit_snapshot" AS "UnitSnapshot",
                       l."quantity" AS "Quantity",
                       l."unlinked_qty" AS "UnlinkedQty",
                       l."planned_delivery_date" AS "PlannedDeliveryDate",
                       latest."ReadyQty" AS "LatestReadyQty"
                FROM "public"."purchase_order_line" l
                LEFT JOIN LATERAL
                (
                    SELECT f."ready_qty" AS "ReadyQty" FROM "public"."supplier_progress_report" f
                    WHERE f."purchase_order_line_id"=l."uid" AND COALESCE(f."is_deleted",FALSE)=FALSE
                    ORDER BY f."sequence" DESC NULLS LAST,{UuidOrder("f.\"uid\"")} DESC NULLS LAST
                    LIMIT 1
                ) latest ON TRUE
                WHERE l."purchase_order_id"=@PurchaseOrderId
                ORDER BY l."line_number" ASC NULLS FIRST,{UuidOrder("l.\"uid\"")} ASC NULLS FIRST;

        SELECT link."uid" AS "LinkId",
                       link."target_line_id" AS "PurchaseOrderLineId",
                       link."net_demand_id" AS "NetDemandId",
                       link."is_deleted" AS "IsDeleted",
                       n."gross_demand_id" AS "GrossDemandId",
                       link."link_status" AS "LinkStatus",
                       link."allocated_qty" AS "AllocatedQty",
                       link."released_qty" AS "ReleasedQty",
                       link."allocated_qty"-link."released_qty" AS "EffectiveQty",
                       g."source_type" AS "SourceType",
                       g."source_uid" AS "SourceUid",
                       g."source_line_id" AS "SourceLineId",
                       g."source_no" AS "SourceNo",
                       g."source_line_number" AS "SourceLineNumber",
                       n."decomposition_revision" AS "DecompositionRevision",
                       g."source_version" AS "SourceVersion",
                       n."source_demand_version_snapshot" AS "SourceDemandVersionSnapshot",
                       n."source_version_snapshot" AS "SourceVersionSnapshot",
                       g."demand_basis_fingerprint" AS "DemandBasisFingerprint",
                       n."source_demand_basis_fingerprint_snapshot" AS "DemandBasisFingerprintSnapshot",
                       g."unit_snapshot" AS "UnitSnapshot",
                       link."demand_required_date" AS "DemandRequiredDate",
                       link."cancelled_at" AS "CancelledAt",
                       link."cancelled_by" AS "CancelledBy",
                       link."cancelled_reason" AS "CancelledReason",
                       CASE WHEN n."uid" IS NULL OR g."uid" IS NULL
                           OR g."source_uid" IS NULL OR g."source_uid"='00000000-0000-0000-0000-000000000000'
                           OR g."source_line_id" IS NULL OR g."source_line_id"='00000000-0000-0000-0000-000000000000'
                           OR NOT COALESCE({SwiftExport.Infrastructure.Repositories.DemandPlanning.MaterialDemandBasisSql.IsApplicable("n","g")},FALSE)
                           OR NULLIF(btrim(l."unit_snapshot",' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                           OR COALESCE(NULLIF(btrim(p."unit",' ') COLLATE public.erp_dictionary_ci,''),
                               CASE WHEN n."parent_net_demand_id" IS NULL THEN NULLIF(btrim(g."unit_snapshot",' ') COLLATE public.erp_dictionary_ci,'') END) IS NULL
                           OR btrim(l."unit_snapshot",' ') COLLATE public.erp_dictionary_ci<>COALESCE(NULLIF(btrim(p."unit",' ') COLLATE public.erp_dictionary_ci,''),
                               CASE WHEN n."parent_net_demand_id" IS NULL THEN NULLIF(btrim(g."unit_snapshot",' ') COLLATE public.erp_dictionary_ci,'') END)
                           OR link."target_head_id"<>l."purchase_order_id"
                           OR link."allocated_qty"<0 OR link."released_qty"<0 OR link."released_qty">link."allocated_qty"
                           THEN TRUE ELSE FALSE END AS "NeedsReview",
                       CASE WHEN n."uid" IS NULL OR g."uid" IS NULL THEN '需求来源记录缺失。'
                            WHEN g."source_uid" IS NULL OR g."source_uid"='00000000-0000-0000-0000-000000000000'
                              OR g."source_line_id" IS NULL OR g."source_line_id"='00000000-0000-0000-0000-000000000000'
                              THEN '需求来源身份缺失。'
                            WHEN NOT COALESCE({SwiftExport.Infrastructure.Repositories.DemandPlanning.MaterialDemandBasisSql.IsApplicable("n","g")},FALSE)
                              THEN '来源身份、状态或实物指纹与当前来源不一致。'
                            WHEN NULLIF(btrim(l."unit_snapshot",' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                              OR COALESCE(NULLIF(btrim(p."unit",' ') COLLATE public.erp_dictionary_ci,''),
                                  CASE WHEN n."parent_net_demand_id" IS NULL THEN NULLIF(btrim(g."unit_snapshot",' ') COLLATE public.erp_dictionary_ci,'') END) IS NULL
                              OR btrim(l."unit_snapshot",' ') COLLATE public.erp_dictionary_ci<>COALESCE(NULLIF(btrim(p."unit",' ') COLLATE public.erp_dictionary_ci,''),
                                  CASE WHEN n."parent_net_demand_id" IS NULL THEN NULLIF(btrim(g."unit_snapshot",' ') COLLATE public.erp_dictionary_ci,'') END)
                              THEN '物料与采购行单位不一致。'
                            WHEN link."target_head_id"<>l."purchase_order_id" THEN 'Link 采购头行身份不一致。'
                            WHEN link."allocated_qty"<0 OR link."released_qty"<0 OR link."released_qty">link."allocated_qty"
                              THEN '需求贡献或释放数量异常。'
                            ELSE NULL END AS "ReviewReason"
                FROM "public"."demand_execution_link" link
                INNER JOIN "public"."purchase_order_line" l ON l."uid"=link."target_line_id" AND l."purchase_order_id"=@PurchaseOrderId
                LEFT JOIN "public"."net_demand" n ON n."uid"=link."net_demand_id"
                LEFT JOIN "public"."gross_demand" g ON g."uid"=n."gross_demand_id"
                LEFT JOIN "public"."product" p ON p."uid"=n."product_id" AND p."is_deleted"=FALSE
                WHERE rtrim(link."target_type",' ') COLLATE public.erp_dictionary_ci='PO'
                ORDER BY l."line_number" ASC NULLS FIRST,link."demand_required_date" ASC NULLS FIRST,{UuidOrder("link.\"uid\"")} ASC NULLS FIRST;

        SELECT f."uid" AS "Uid",
                       f."purchase_order_line_id" AS "PurchaseOrderLineId",
                       f."sequence" AS "Sequence",
                       f."arrangement_status" AS "ArrangementStatus",
                       f."ready_qty" AS "ReadyQty",
                       f."expected_ready_date" AS "ExpectedReadyDate",
                       f."supplier_reported_date" AS "SupplierReportedDate",
                       f."note" AS "Note",
                       f."is_correction" AS "IsCorrection",
                       f."correction_reason" AS "CorrectionReason",
                       f."created_date" AS "CreatedDate",
                       f."created_by" AS "CreatedBy"
                FROM "public"."supplier_progress_report" f
                INNER JOIN "public"."purchase_order_line" l ON l."uid"=f."purchase_order_line_id" AND l."purchase_order_id"=@PurchaseOrderId
                WHERE COALESCE(f."is_deleted",FALSE)=FALSE
                ORDER BY l."line_number" ASC NULLS FIRST,f."sequence" DESC NULLS LAST,{UuidOrder("f.\"uid\"")} DESC NULLS LAST;

        SELECT h."uid" AS "InboundDocumentId",
                       l."uid" AS "InboundLineId",
                       l."source_line_id" AS "PurchaseOrderLineId",
                       h."document_no" AS "DocumentNo",
                       h."posting_status" AS "PostingStatus",
                       l."quantity" AS "Quantity",
                       CASE WHEN h."posting_status"=1 THEN l."quantity" ELSE 0 END AS "NetQuantity",
                       l."unit_snapshot" AS "UnitSnapshot",
                       h."warehouse_name_snapshot" AS "WarehouseNameSnapshot",
                       h."receipt_date" AS "ReceiptDate",
                       h."posted_date" AS "PostedDate",
                       h."posted_by" AS "PostedBy",
                       h."reversed_date" AS "ReversedDate",
                       h."reversed_by" AS "ReversedBy",
                       h."reverse_reason" AS "ReverseReason",
                       h."trigger_packing_list_id" AS "TriggerPackingListId"
                FROM "public"."inbound_document_line" l
                INNER JOIN "public"."inbound_document" h ON h."uid"=l."inbound_document_id"
                INNER JOIN "public"."purchase_order_line" p ON p."uid"=l."source_line_id" AND p."purchase_order_id"=@PurchaseOrderId
                WHERE h."document_type"=@PurchaseReceipt AND h."posting_status"=ANY(@FormalPostingStatuses)
                  AND COALESCE(h."is_deleted",FALSE)=FALSE AND COALESCE(l."is_deleted",FALSE)=FALSE
                ORDER BY h."receipt_date" ASC NULLS FIRST,{UuidOrder("h.\"uid\"")} ASC NULLS FIRST,l."line_number" ASC NULLS FIRST,{UuidOrder("l.\"uid\"")} ASC NULLS FIRST;

        SELECT log."uid" AS "Uid",
                       log."operation_name" AS "OperationName",
                       log."operation_type" AS "OperationType",
                       log."reason" AS "Reason",
                       log."summary" AS "Summary",
                       log."user_name_snapshot" AS "UserName",
                       log."occurred_date" AS "OccurredDate"
                FROM "public"."business_operation_log" log
                WHERE COALESCE(log."is_deleted",FALSE)=FALSE AND (log."aggregate_uid"=@PurchaseOrderId
                    OR CASE WHEN log.module='Procurement.OrderManagement' AND log.operation_type='CreateDraftsFromDemand'
                            AND log.related_objects_json IS JSON ARRAY THEN log.related_objects_json::jsonb
                        @> jsonb_build_array(jsonb_build_object('PurchaseOrderId',@PurchaseOrderId)) ELSE FALSE END)
                ORDER BY log."occurred_date" DESC NULLS LAST,{UuidOrder("log.\"uid\"")} DESC NULLS LAST;
        """;
    /// <summary>保留SQL Server uniqueidentifier的SqlGuid字节比较顺序。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(x => $"get_byte(uuid_send({column}),{x})")) + "]";

    /// <summary>独立克隆命令参数，明确uuid及integer数组，不修改共享参数或事务组件。</summary>
    private sealed class TraceParameters(PostgreSqlParameters parameters) : SqlMapper.IDynamicParameters
    {
        /// <summary>明确驱动类型，避免装饰连接使Dapper展开数组。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            parameters.AddParameters(command, identity);
            foreach (NpgsqlParameter parameter in command.Parameters)
                parameter.NpgsqlDbType = parameter.ParameterName switch
                {
                    "PurchaseOrderId" => NpgsqlDbType.Uuid,
                    "FormalPostingStatuses" => NpgsqlDbType.Array | NpgsqlDbType.Integer,
                    _ => NpgsqlDbType.Integer
                };
        }
    }
}
