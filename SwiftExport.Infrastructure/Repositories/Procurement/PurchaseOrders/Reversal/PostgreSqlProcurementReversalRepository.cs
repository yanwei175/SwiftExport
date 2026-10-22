using Dapper;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Reversal;

/// <summary>采购确认、反向和补关联共用的 PostgreSQL 事实锁及反向持久化；保持原锁序和副作用，不接管联合准备或事务。</summary>
public sealed class PostgreSqlProcurementReversalRepository(RepositoryBaseContext context) : IProcurementReversalRepository
{
    /// <summary>返回全部采购行关联的异常入库头，检查同头全部有效行，供删行按关联头定位而不缩小事实锁。</summary>
    private const string UnattributedInboundSql = """
        SELECT h.uid FROM public.inbound_document h
            JOIN public.inbound_document_line l ON l.inbound_document_id=h.uid
            LEFT JOIN LATERAL (SELECT p.uid FROM public.purchase_order_line p WHERE p.uid=l.source_line_id FOR SHARE) p ON TRUE
            WHERE h.is_deleted=FALSE AND l.is_deleted=FALSE
                AND EXISTS(SELECT 1 FROM public.inbound_document_line scope
                    WHERE scope.inbound_document_id=h.uid AND scope.source_line_id=ANY(@lineIds) FOR SHARE OF scope)
                AND ((h.posting_status IN(1,8) AND h.document_type IN(11,12))
                    OR (h.document_type=10 AND p.uid IS NULL AND (h.posting_status IN(1,8) OR h.lifecycle_status=0)))
            ORDER BY h.uid,l.uid FOR SHARE OF h,l;
        """;

    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string GrossDemandColumns = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "lifecycle_status" AS "LifecycleStatus",
        "source_type" AS "SourceType",
        "source_uid" AS "SourceUid",
        "source_no" AS "SourceNo",
        "source_line_id" AS "SourceLineId",
        "source_line_number" AS "SourceLineNumber",
        "source_version" AS "SourceVersion",
        "demand_basis_fingerprint" AS "DemandBasisFingerprint",
        "customer_id" AS "CustomerId",
        "product_id" AS "ProductId",
        "is_non_standard" AS "IsNonStandard",
        "sku_snapshot" AS "SkuSnapshot",
        "link_sku_snapshot" AS "LinkSkuSnapshot",
        "customer_sku_snapshot" AS "CustomerSkuSnapshot",
        "description_snapshot" AS "DescriptionSnapshot",
        "color_snapshot" AS "ColorSnapshot",
        "specification_snapshot" AS "SpecificationSnapshot",
        "image_path_snapshot" AS "ImagePathSnapshot",
        "unit_snapshot" AS "UnitSnapshot",
        "demand_qty" AS "DemandQty",
        "required_date" AS "RequiredDate",
        "remark" AS "Remark",
        "lifecycle_changed_at" AS "LifecycleChangedAt",
        "lifecycle_changed_by" AS "LifecycleChangedBy",
        "lifecycle_changed_reason" AS "LifecycleChangedReason"
    """;
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string NetDemandColumns = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "gross_demand_id" AS "GrossDemandId",
        "source_demand_version_snapshot" AS "SourceDemandVersionSnapshot",
        "source_version_snapshot" AS "SourceVersionSnapshot",
        "source_demand_basis_fingerprint_snapshot" AS "SourceDemandBasisFingerprintSnapshot",
        "decomposition_revision" AS "DecompositionRevision",
        "line_number" AS "LineNumber",
        "lifecycle_status" AS "LifecycleStatus",
        "parent_net_demand_id" AS "ParentNetDemandId",
        "root_net_demand_id" AS "RootNetDemandId",
        "delivery_target_net_demand_id" AS "DeliveryTargetNetDemandId",
        "delivery_mode" AS "DeliveryMode",
        "product_id" AS "ProductId",
        "suggested_purchase_supplier_id" AS "SuggestedPurchaseSupplierId",
        "bom_line_id" AS "BomLineId",
        "bom_version" AS "BomVersion",
        "bom_path" AS "BomPath",
        "material_type_snapshot" AS "MaterialTypeSnapshot",
        "supply_type_snapshot" AS "SupplyTypeSnapshot",
        "sku_snapshot" AS "SkuSnapshot",
        "parent_sku_snapshot" AS "ParentSkuSnapshot",
        "customer_sku_snapshot" AS "CustomerSkuSnapshot",
        "description_snapshot" AS "DescriptionSnapshot",
        "color_snapshot" AS "ColorSnapshot",
        "specification_snapshot" AS "SpecificationSnapshot",
        "unit_snapshot" AS "UnitSnapshot",
        "gross_qty" AS "GrossQty",
        "net_qty" AS "NetQty",
        "required_date" AS "RequiredDate",
        "remark" AS "Remark",
        "rolled_back_at" AS "RolledBackAt",
        "rolled_back_by" AS "RolledBackBy",
        "rolled_back_reason" AS "RolledBackReason"
    """;
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string DemandExecutionLinkColumns = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "net_demand_id" AS "NetDemandId",
        "execution_request_id" AS "ExecutionRequestId",
        "command_item_id" AS "CommandItemId",
        "target_type" AS "TargetType",
        "target_head_id" AS "TargetHeadId",
        "target_no" AS "TargetNo",
        "target_line_id" AS "TargetLineId",
        "link_status" AS "LinkStatus",
        "allocated_qty" AS "AllocatedQty",
        "released_qty" AS "ReleasedQty",
        "demand_required_date" AS "DemandRequiredDate",
        "planned_delivery_date" AS "PlannedDeliveryDate",
        "cancelled_at" AS "CancelledAt",
        "cancelled_by" AS "CancelledBy",
        "cancelled_reason" AS "CancelledReason",
        "reversed_at" AS "ReversedAt",
        "reversed_by" AS "ReversedBy",
        "reversed_reason" AS "ReversedReason",
        "remark" AS "Remark"
    """;
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string PurchaseOrderHeadColumns = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "origin_kind" AS "OriginKind",
        "customer_id" AS "CustomerId",
        "customer_name_snapshot" AS "CustomerNameSnapshot",
        "active_confirmation_id" AS "ActiveConfirmationId",
        "packaging_requirements" AS "PackagingRequirements",
        "confirmation_status" AS "ConfirmationStatus",
        "lifecycle_status" AS "LifecycleStatus",
        "order_no" AS "OrderNo",
        "bo_no" AS "BoNo",
        "customer_po_no" AS "CustomerPoNo",
        "customer_code" AS "CustomerCode",
        "currency" AS "Currency",
        "discount_rate" AS "DiscountRate",
        "discount_amount" AS "DiscountAmount",
        "supplier_id" AS "SupplierId",
        "supplier_code" AS "SupplierCode",
        "supplier_name" AS "SupplierName",
        "demand_source_type" AS "DemandSourceType",
        "demand_source_uid" AS "DemandSourceUid",
        "demand_source_no" AS "DemandSourceNo",
        "execution_request_id" AS "ExecutionRequestId",
        "so_person" AS "SoPerson",
        "po_person" AS "PoPerson",
        "inletr_hose_type" AS "InletrHoseType",
        "aerator_type" AS "AeratorType",
        "pallet_type" AS "PalletType",
        "box_type" AS "BoxType",
        "label_type" AS "LabelType",
        "im_type" AS "IMType",
        "carton_type" AS "CartonType",
        "logo_type" AS "LogoType",
        "reviewed_by" AS "ReviewedBy",
        "po_folder" AS "PoFolder",
        "zhe_kou_bei_zhu" AS "ZheKouBeiZhu",
        "remark" AS "Remark",
        "order_date" AS "OrderDate",
        "expected_date" AS "ExpectedDate",
        "reply_date" AS "ReplyDate",
        "confirmed_date" AS "ConfirmedDate",
        "confirmed_by" AS "ConfirmedBy"
    """;
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string InboundDocumentLineColumns = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "inbound_document_id" AS "InboundDocumentId",
        "line_number" AS "LineNumber",
        "product_id" AS "ProductId",
        "sku_snapshot" AS "SkuSnapshot",
        "description_snapshot" AS "DescriptionSnapshot",
        "unit_snapshot" AS "UnitSnapshot",
        "quantity" AS "Quantity",
        "source_line_id" AS "SourceLineId",
        "original_line_id" AS "OriginalLineId"
    """;
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string SupplierProgressReportColumns = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "purchase_order_line_id" AS "PurchaseOrderLineId",
        "sequence" AS "Sequence",
        "arrangement_status" AS "ArrangementStatus",
        "ready_qty" AS "ReadyQty",
        "expected_ready_date" AS "ExpectedReadyDate",
        "supplier_reported_date" AS "SupplierReportedDate",
        "note" AS "Note",
        "is_correction" AS "IsCorrection",
        "correction_reason" AS "CorrectionReason",
        "command_id" AS "CommandId"
    """;

    /// <summary>保持原明确事务门禁，透传取消；不自行开启事务或修改隔离级别。</summary>
    private CommandDefinition Command(string sql, object? args, CancellationToken ct) =>
        new(sql, args, context.Session.Transaction ?? throw new InvalidOperationException("采购事实锁定需要事务。"), cancellationToken: ct);

    /// <summary>按原来源到下游顺序锁定完整事实，集合冲突不暴露部分结果。</summary>
    public Task<(ProcurementReversalFacts? Facts, ServiceErrorDto? Error)> LockAsync(Guid uid, CancellationToken ct) =>
        LockCoreAsync(uid, [], true, ct);

    /// <summary>合并待选物料来源后复用相同锁序，补关联不读取收货、库存或供应商报告。</summary>
    public Task<(ProcurementReversalFacts? Facts, ServiceErrorDto? Error)> LockForAssociationAsync(Guid uid, IReadOnlyCollection<Guid> netDemandIds, CancellationToken ct) =>
        LockCoreAsync(uid, netDemandIds, false, ct);

    /// <summary>先发现来源，再锁来源、需求、Link、采购头行及可选下游；原归属及集合重验不补锁新来源。</summary>
    private async Task<(ProcurementReversalFacts? Facts, ServiceErrorDto? Error)> LockCoreAsync(Guid uid, IReadOnlyCollection<Guid> netDemandIds, bool includeDownstream, CancellationToken ct)
    {
        var discoverParameters = Ids("netDemandIds", netDemandIds);
        discoverParameters.Add("uid", uid);
        // PG 高隔离无法将 SQL Server READCOMMITTED 提示等价翻译；沿当前事务观察，联合准备由主控补验。
        const string discover = """
            SELECT DISTINCT n.gross_demand_id FROM public.demand_execution_link l
            JOIN public.net_demand n ON n.uid=l.net_demand_id
            WHERE rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
                AND (l.target_head_id=@uid OR l.target_line_id IN (SELECT uid FROM public.purchase_order_line WHERE purchase_order_id=@uid))
            UNION SELECT gross_demand_id FROM public.net_demand WHERE uid=ANY(@netDemandIds);
            """;
        var sourceIds = (await context.Session.Connection.QueryAsync<Guid>(Command(discover, discoverParameters, ct))).OrderBy(id => id).ToArray();
        var facts = new ProcurementReversalFacts();
        foreach (var id in sourceIds)
        {
            var source = await context.Session.Connection.QuerySingleOrDefaultAsync<GrossDemand>(Command(
                $"SELECT {GrossDemandColumns} FROM public.gross_demand WHERE uid=@id FOR UPDATE", new { id }, ct));
            if (source is null) throw new InvalidOperationException("采购成品需求缺失，不能完成操作。");
            facts.Sources.Add(source);
        }
        foreach (var id in sourceIds)
            facts.Demands.AddRange(await context.Session.Connection.QueryAsync<NetDemand>(Command(
                $"SELECT {NetDemandColumns} FROM public.net_demand WHERE gross_demand_id=@id ORDER BY {UuidOrder("uid")} FOR UPDATE", new { id }, ct)));
        var demandIds = facts.Demands.Select(demand => demand.Uid).ToArray();
        if (demandIds.Length > 0)
            facts.Links.AddRange(await context.Session.Connection.QueryAsync<DemandExecutionLink>(Command(
                $"SELECT {DemandExecutionLinkColumns} FROM public.demand_execution_link WHERE net_demand_id=ANY(@demandIds) ORDER BY {UuidOrder("uid")} FOR UPDATE",
                Ids("demandIds", demandIds), ct)));

        // 不依赖 NetDemand JOIN，完整读取本单坏引用，禁止把损坏关系当作独立采购。
        var ownLinkSql = $"""
            SELECT {DemandExecutionLinkColumns} FROM public.demand_execution_link
            WHERE rtrim(target_type,' ') COLLATE public.erp_dictionary_ci='PO'
                AND (target_head_id=@uid OR target_line_id IN (SELECT uid FROM public.purchase_order_line WHERE purchase_order_id=@uid))
            ORDER BY {UuidOrder("uid")} FOR UPDATE;
            """;
        var ownLinks = (await context.Session.Connection.QueryAsync<DemandExecutionLink>(Command(ownLinkSql, new { uid }, ct))).ToList();
        if (ownLinks.Any(link => !demandIds.Contains(link.NetDemandId)))
            return (null, new ServiceErrorDto { Code = ServiceErrorCodes.ConcurrencyConflict, Message = "采购引用已变化或物料来源缺失，请重新预览。", Field = "ExpectedVersion" });
        facts.Links = facts.Links.Concat(ownLinks).DistinctBy(link => link.Uid).ToList();
        facts.Head = await context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>(Command(
            $"SELECT {PurchaseOrderHeadColumns} FROM public.purchase_order_head WHERE uid=@uid FOR UPDATE", new { uid }, ct));
        facts.Lines = (await context.Session.Connection.QueryAsync<PurchaseOrderLine>(Command(
            $"""
                SELECT l."uid" AS "Uid",
                l."version" AS "Version",
                l."is_deleted" AS "IsDeleted",
                l."created_date" AS "CreatedDate",
                l."created_by" AS "CreatedBy",
                l."updated_date" AS "UpdatedDate",
                l."updated_by" AS "UpdatedBy",
                l."unlinked_qty" AS "UnlinkedQty",
                l."purchase_order_id" AS "PurchaseOrderId",
                l."line_number" AS "LineNumber",
                l."order_no" AS "OrderNo",
                l."product_id" AS "ProductId",
                l."sku" AS "Sku",
                l."customer_sku" AS "CustomerSku",
                l."link_sku" AS "LinkSku",
                l."supplier_sku" AS "SupplierSku",
                l."description_zh" AS "DescriptionZh",
                l."finish_zh" AS "FinishZh",
                l."features_zh" AS "FeaturesZh",
                l."unit_snapshot" AS "UnitSnapshot",
                l."quantity" AS "Quantity",
                l."unit_price" AS "UnitPrice",
                l."tax_price" AS "TaxPrice",
                l."unit_amount" AS "UnitAmount",
                l."tax_amount" AS "TaxAmount",
                l."planned_delivery_date" AS "PlannedDeliveryDate",
                l."delivery_target_supplier_id" AS "DeliveryTargetSupplierId",
                l."delivery_supplier_code_snapshot" AS "DeliverySupplierCodeSnapshot",
                l."delivery_supplier_name_snapshot" AS "DeliverySupplierNameSnapshot",
                l."sub_note" AS "SubNote",
                l."supply_type" AS "SupplyType",
                l."material_type" AS "MaterialType",
                l."source_type" AS "SourceType",
                l."source_no" AS "SourceNo",
                l."source_uid" AS "SourceUid",
                l."source_line_id" AS "SourceLineId",
                l."source_line_number" AS "SourceLineNumber" FROM public.purchase_order_line l WHERE l.purchase_order_id=@uid ORDER BY {UuidOrder("l.uid")} FOR UPDATE OF l
            """, new { uid }, ct))).ToList();
        var finalLinks = (await context.Session.Connection.QueryAsync<DemandExecutionLink>(Command(ownLinkSql, new { uid }, ct))).ToList();
        if (!ownLinks.Select(link => (link.Uid, link.Version, link.NetDemandId, link.TargetLineId)).ToHashSet()
            .SetEquals(finalLinks.Select(link => (link.Uid, link.Version, link.NetDemandId, link.TargetLineId))))
            return (null, new ServiceErrorDto { Code = ServiceErrorCodes.ConcurrencyConflict, Message = "锁定过程中采购引用集合发生变化，请重新预览。", Field = "ExpectedVersion" });
        if (!includeDownstream) return (facts, null);
        facts.HasConfirmationHistory = await context.Session.Connection.ExecuteScalarAsync<bool>(Command(
            $"SELECT {PurchaseDraftDeletionSql.ConfirmationHistory("@uid")};",new {uid},ct));
        facts.HasDownstreamHistory = await context.Session.Connection.ExecuteScalarAsync<bool>(Command(
            $"SELECT {PurchaseDraftDeletionSql.DownstreamHistory("@uid")};",new {uid},ct));

        var lineIds = facts.Lines.Select(line => line.Uid).ToArray();
        // 先限定相关入库头，再检查同头坏行，不能让无关头阻断，也不能仅过滤采购行而漏掉同头孤立来源。
        facts.UnattributedInboundHeadIds = (await context.Session.Connection.QueryAsync<Guid>(Command(
            UnattributedInboundSql, Ids("lineIds", lineIds), ct))).ToHashSet();
        if (lineIds.Length == 0) return (facts, null);
        facts.Inbounds = (await context.Session.Connection.QueryAsync<InboundDocument>(Command($"""
            SELECT h."uid" AS "Uid", h."version" AS "Version", h."is_deleted" AS "IsDeleted", h."created_date" AS "CreatedDate", h."created_by" AS "CreatedBy", h."updated_date" AS "UpdatedDate", h."updated_by" AS "UpdatedBy", h."document_no" AS "DocumentNo", h."document_type" AS "DocumentType", h."creation_mode" AS "CreationMode", h."lifecycle_status" AS "LifecycleStatus", h."posting_status" AS "PostingStatus", h."warehouse_site_id" AS "WarehouseSiteId", h."warehouse_code_snapshot" AS "WarehouseCodeSnapshot", h."warehouse_name_snapshot" AS "WarehouseNameSnapshot", h."receipt_date" AS "ReceiptDate", h."remark" AS "Remark", h."posted_date" AS "PostedDate", h."posted_by" AS "PostedBy", h."reversed_date" AS "ReversedDate", h."reversed_by" AS "ReversedBy", h."reverse_reason" AS "ReverseReason", h."trigger_packing_list_id" AS "TriggerPackingListId", h."command_id" AS "CommandId" FROM public.inbound_document h
            WHERE EXISTS(SELECT 1 FROM public.inbound_document_line l
                WHERE l.inbound_document_id=h.uid AND l.source_line_id=ANY(@lineIds) FOR SHARE)
            ORDER BY {UuidOrder("h.uid")} FOR UPDATE OF h;
            """, Ids("lineIds", lineIds), ct))).ToList();
        var inboundIds = facts.Inbounds.Select(inbound => inbound.Uid).ToArray();
        if (inboundIds.Length > 0)
        {
            facts.InboundLines = (await context.Session.Connection.QueryAsync<InboundDocumentLine>(Command(
                $"SELECT {InboundDocumentLineColumns} FROM public.inbound_document_line WHERE inbound_document_id=ANY(@inboundIds) ORDER BY {UuidOrder("uid")} FOR UPDATE",
                Ids("inboundIds", inboundIds), ct))).ToList();
            facts.Ledgers = (await context.Session.Connection.QueryAsync<InventoryLedgerEntry>(Command($"""
                SELECT e."uid" AS "Uid", e."version" AS "Version", e."is_deleted" AS "IsDeleted", e."created_date" AS "CreatedDate", e."created_by" AS "CreatedBy", e."updated_date" AS "UpdatedDate", e."updated_by" AS "UpdatedBy", e."sequence" AS "Sequence", e."warehouse_site_id" AS "WarehouseSiteId", e."product_id" AS "ProductId", e."transaction_type" AS "TransactionType", e."quantity_delta" AS "QuantityDelta", e."balance_after" AS "BalanceAfter", e."source_document_type" AS "SourceDocumentType", e."source_document_id" AS "SourceDocumentId", e."source_line_id" AS "SourceLineId", e."source_document_no_snapshot" AS "SourceDocumentNoSnapshot", e."reversal_of_ledger_id" AS "ReversalOfLedgerId", e."replacement_of_ledger_id" AS "ReplacementOfLedgerId", e."correction_group_id" AS "CorrectionGroupId", e."command_id" AS "CommandId", e."command_item_id" AS "CommandItemId", e."request_fingerprint" AS "RequestFingerprint", e."reason" AS "Reason", e."occurred_date" AS "OccurredDate", e."warehouse_code_snapshot" AS "WarehouseCodeSnapshot", e."warehouse_name_snapshot" AS "WarehouseNameSnapshot", e."sku_snapshot" AS "SkuSnapshot", e."product_name_snapshot" AS "ProductNameSnapshot", e."unit_snapshot" AS "UnitSnapshot" FROM public.inventory_ledger_entry e
                WHERE e.source_document_id=ANY(@inboundIds)
                    OR e.source_line_id IN (SELECT uid FROM public.inbound_document_line WHERE inbound_document_id=ANY(@inboundIds))
                    OR e.reversal_of_ledger_id IN (SELECT uid FROM public.inventory_ledger_entry WHERE source_document_id=ANY(@inboundIds))
                    OR e.replacement_of_ledger_id IN (SELECT uid FROM public.inventory_ledger_entry WHERE source_document_id=ANY(@inboundIds))
                FOR SHARE OF e;
                """, Ids("inboundIds", inboundIds), ct))).ToList();
        }
        facts.Reports = (await context.Session.Connection.QueryAsync<SupplierProgressReport>(Command(
            $"SELECT {SupplierProgressReportColumns} FROM public.supplier_progress_report WHERE purchase_order_line_id=ANY(@lineIds) AND is_deleted=FALSE ORDER BY {UuidOrder("purchase_order_line_id")},sequence ASC NULLS FIRST FOR SHARE",
            Ids("lineIds", lineIds), ct))).ToList();
        return (facts, null);
    }

    /// <summary>在原事务内应用已校验反向；失败返回原写入结果，保留逐步实体副作用，由父用例回滚数据库。</summary>
    public async Task<RepositoryWriteResult> ApplyAsync(ProcurementReversalFacts facts, ProcurementReversalOperation operation, IReadOnlyCollection<Guid> lineIds, string reason, CancellationToken ct)
    {
        var head = facts.Head!;
        var oldVersion = head.Version;
        context.AuditStamp.StampForUpdate(head);
        var cancel = operation == ProcurementReversalOperation.CancelConfirmation;
        var delete = operation is ProcurementReversalOperation.DeleteLines or ProcurementReversalOperation.DeleteDraft;
        var deleteHead = operation == ProcurementReversalOperation.DeleteDraft;
        if (await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE public.purchase_order_head SET lifecycle_status=CASE WHEN @Void THEN 9 ELSE lifecycle_status END,
                is_deleted=CASE WHEN @DeleteDraft THEN TRUE ELSE is_deleted END,
                confirmation_status=CASE WHEN @Cancel THEN 0 ELSE confirmation_status END,
                active_confirmation_id=CASE WHEN @Cancel THEN NULL ELSE active_confirmation_id END,
                confirmed_date=CASE WHEN @Cancel THEN NULL ELSE confirmed_date END,
                confirmed_by=CASE WHEN @Cancel THEN NULL ELSE confirmed_by END,
                updated_date=@UpdatedDate,updated_by=@UpdatedBy,version=version+1
            WHERE uid=@Uid AND version=@Version AND lifecycle_status=0 AND is_deleted=FALSE;
            """, HeadParameters(head, cancel, operation == ProcurementReversalOperation.Void, deleteHead), ct)) != 1)
            return RepositoryWriteResult.Fail(head.Uid, head.Version, ServiceErrorCodes.ConcurrencyConflict, "采购状态已变化，反向操作未执行。");
        head.Version++;
        if (deleteHead) head.IsDeleted=true;
        if (cancel)
        {
            head.ConfirmationStatus = ConfirmationStatusEnum.待确认;
            head.ActiveConfirmationId = null; head.ConfirmedDate = null; head.ConfirmedBy = null;
        }
        else
        {
            foreach (var link in facts.Links.Where(x => x.TargetType == "PO" && x.TargetHeadId == head.Uid && lineIds.Contains(x.TargetLineId) && !x.IsDeleted && x.LinkStatus == DemandExecutionLinkStatusEnum.Active))
            {
                context.AuditStamp.StampForUpdate(link);
                if (await context.Session.Connection.ExecuteAsync(Command("""
                    UPDATE public.demand_execution_link SET link_status=8,released_qty=allocated_qty,cancelled_at=@UpdatedDate,
                        cancelled_by=@UpdatedBy,cancelled_reason=@reason,updated_date=@UpdatedDate,updated_by=@UpdatedBy,version=version+1
                    WHERE uid=@Uid AND version=@Version AND link_status=0 AND is_deleted=FALSE;
                    """, ReleaseParameters(link, reason), ct)) != 1)
                    return RepositoryWriteResult.Fail(link.Uid, link.Version, ServiceErrorCodes.ConcurrencyConflict, "采购贡献已变化，解除引用未执行。");
                link.Version++; link.LinkStatus = DemandExecutionLinkStatusEnum.Cancelled; link.ReleasedQty = link.AllocatedQty;
                link.CancelledAt = link.UpdatedDate; link.CancelledBy = link.UpdatedBy; link.CancelledReason = reason;
            }
            if (delete)
            {
                foreach (var line in facts.Lines.Where(x => lineIds.Contains(x.Uid)))
                {
                    context.AuditStamp.StampForUpdate(line);
                    if (await context.Session.Connection.ExecuteAsync(Command("""
                        UPDATE public.purchase_order_line SET is_deleted=TRUE,version=version+1,updated_date=@UpdatedDate,updated_by=@UpdatedBy
                        WHERE uid=@Uid AND purchase_order_id=@PurchaseOrderId AND version=@Version AND is_deleted=FALSE;
                        """, Parameters(line, "Uid", "PurchaseOrderId", "Version", "UpdatedDate", "UpdatedBy"), ct)) != 1) return RepositoryWriteResult.Fail(line.Uid, line.Version, ServiceErrorCodes.ConcurrencyConflict, "采购行已变化，删除未执行。");
                    line.IsDeleted = true; line.Version++;
                }
                head.ExpectedDate = facts.Lines.Where(x => !x.IsDeleted).Min(x => x.PlannedDeliveryDate);
                await context.Session.Connection.ExecuteAsync(Command(
                    "UPDATE public.purchase_order_head SET expected_date=@ExpectedDate WHERE uid=@Uid;", Parameters(head, "Uid", "ExpectedDate"), ct));
            }
            else head.LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Voided;
        }
        return RepositoryWriteResult.Ok(head.Uid, oldVersion, head.Version);
    }

    /// <summary>显式绑定 UUID 数组，保留原输入集合，不展开或去除空身份。</summary>
    private static PostgreSqlParameters Ids(string name, IEnumerable<Guid> ids)
    {
        var parameters = new PostgreSqlParameters();
        parameters.AddIds(name, ids);
        return parameters;
    }

    /// <summary>只绑定当前写入使用的属性；日期按已有无时区墙上时间绑定，不改变实体。</summary>
    private static PostgreSqlParameters Parameters(BaseEntity entity, params string[] properties)
    {
        var metadata = EntityMetadataCache.GetMetadata(entity.GetType());
        var parameters = new PostgreSqlParameters();
        foreach (var property in properties)
        {
            var column = metadata.GetProperty(property);
            var value = column.Property.GetValue(entity);
            if ((Nullable.GetUnderlyingType(column.Property.PropertyType) ?? column.Property.PropertyType) == typeof(DateTime))
                parameters.AddTimestamp(property, (DateTime?)value);
            else parameters.Add(property, value);
        }
        return parameters;
    }

    /// <summary>绑定原头版本及审计与取消/作废布尔分支，不使用调用方期望版本覆盖实体版本。</summary>
    private static PostgreSqlParameters HeadParameters(PurchaseOrderHead head, bool cancel, bool isVoid, bool deleteDraft)
    {
        var parameters = Parameters(head, "Uid", "Version", "UpdatedDate", "UpdatedBy");
        parameters.Add("Cancel", cancel);
        parameters.Add("Void", isVoid);
        parameters.Add("DeleteDraft",deleteDraft);
        return parameters;
    }

    /// <summary>绑定原贡献解除审计和原因，不新增已释放量参数或计算口径。</summary>
    private static PostgreSqlParameters ReleaseParameters(DemandExecutionLink link, string reason)
    {
        var parameters = Parameters(link, "Uid", "Version", "UpdatedDate", "UpdatedBy");
        parameters.Add("reason", reason);
        return parameters;
    }

    /// <summary>数据库结果及锁行顺序保留源 SqlGuid 字节序，来源发现的.NET排序另按原方法执行。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',',
        new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(index => $"get_byte(uuid_send({column}),{index})")) + "]";
}

