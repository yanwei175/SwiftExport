using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;

/// <summary>完整实现需求转采购草稿的PostgreSQL八方法接口；借用当前会话，保留固定写列及生命周期门禁，不接管来源事务。</summary>
public sealed class PostgreSqlPurchaseOrderDraftRepository(RepositoryBaseContext context) : IPurchaseOrderDraftRepository
{
    /// <summary>本仓储固定持久化列及参数，保留原写入范围。</summary>
    private const string HeadInsertSql = """
        INSERT INTO "public"."purchase_order_head" ("uid",
        "version",
        "is_deleted",
        "created_date",
        "created_by",
        "updated_date",
        "updated_by",
        "origin_kind",
        "confirmation_status",
        "lifecycle_status",
        "order_no",
        "bo_no",
        "customer_po_no",
        "customer_id",
        "customer_code",
        "customer_name_snapshot",
        "currency",
        "discount_rate",
        "discount_amount",
        "supplier_id",
        "supplier_code",
        "supplier_name",
        "demand_source_type",
        "demand_source_uid",
        "demand_source_no",
        "execution_request_id",
        "so_person",
        "po_person",
        "inletr_hose_type",
        "aerator_type",
        "pallet_type",
        "box_type",
        "label_type",
        "im_type",
        "carton_type",
        "logo_type",
        "reviewed_by",
        "po_folder",
        "zhe_kou_bei_zhu",
        "remark",
        "order_date",
        "expected_date",
        "reply_date",
        "confirmed_date",
        "confirmed_by") VALUES (@Uid,
        @Version,
        @IsDeleted,
        @CreatedDate,
        @CreatedBy,
        @UpdatedDate,
        @UpdatedBy,
        @OriginKind,
        @ConfirmationStatus,
        @LifecycleStatus,
        @OrderNo,
        @BoNo,
        @CustomerPoNo,
        @CustomerId,
        @CustomerCode,
        @CustomerNameSnapshot,
        @Currency,
        @DiscountRate,
        @DiscountAmount,
        @SupplierId,
        @SupplierCode,
        @SupplierName,
        @DemandSourceType,
        @DemandSourceUid,
        @DemandSourceNo,
        @ExecutionRequestId,
        @SoPerson,
        @PoPerson,
        @InletrHoseType,
        @AeratorType,
        @PalletType,
        @BoxType,
        @LabelType,
        @IMType,
        @CartonType,
        @LogoType,
        @ReviewedBy,
        @PoFolder,
        @ZheKouBeiZhu,
        @Remark,
        @OrderDate,
        @ExpectedDate,
        @ReplyDate,
        @ConfirmedDate,
        @ConfirmedBy);
    """;

    private static readonly EntityMetadata Head = EntityMetadataCache.GetMetadata(typeof(PurchaseOrderHead));
    private static readonly EntityMetadata Line = EntityMetadataCache.GetMetadata(typeof(PurchaseOrderLine));
    // 原头投影只含44列；OriginKind、确认批次和包装要求不是本读取合同的一部分。
    private const string HeadProjection = """
        h."uid" AS "Uid",
        h."version" AS "Version",
        h."is_deleted" AS "IsDeleted",
        h."created_date" AS "CreatedDate",
        h."created_by" AS "CreatedBy",
        h."updated_date" AS "UpdatedDate",
        h."updated_by" AS "UpdatedBy",
        h."confirmation_status" AS "ConfirmationStatus",
        h."lifecycle_status" AS "LifecycleStatus",
        h."order_no" AS "OrderNo",
        h."bo_no" AS "BoNo",
        h."customer_po_no" AS "CustomerPoNo",
        h."customer_id" AS "CustomerId",
        h."customer_code" AS "CustomerCode",
        h."customer_name_snapshot" AS "CustomerNameSnapshot",
        h."currency" AS "Currency",
        h."discount_rate" AS "DiscountRate",
        h."discount_amount" AS "DiscountAmount",
        h."supplier_id" AS "SupplierId",
        h."supplier_code" AS "SupplierCode",
        h."supplier_name" AS "SupplierName",
        h."demand_source_type" AS "DemandSourceType",
        h."demand_source_uid" AS "DemandSourceUid",
        h."demand_source_no" AS "DemandSourceNo",
        h."execution_request_id" AS "ExecutionRequestId",
        h."so_person" AS "SoPerson",
        h."po_person" AS "PoPerson",
        h."inletr_hose_type" AS "InletrHoseType",
        h."aerator_type" AS "AeratorType",
        h."pallet_type" AS "PalletType",
        h."box_type" AS "BoxType",
        h."label_type" AS "LabelType",
        h."im_type" AS "IMType",
        h."carton_type" AS "CartonType",
        h."logo_type" AS "LogoType",
        h."reviewed_by" AS "ReviewedBy",
        h."po_folder" AS "PoFolder",
        h."zhe_kou_bei_zhu" AS "ZheKouBeiZhu",
        h."remark" AS "Remark",
        h."order_date" AS "OrderDate",
        h."expected_date" AS "ExpectedDate",
        h."reply_date" AS "ReplyDate",
        h."confirmed_date" AS "ConfirmedDate",
        h."confirmed_by" AS "ConfirmedBy"
        """;

    /// <summary>普通读取指定来源和供应商的活动待确认安全追加候选；金额来自未删行快照，历史收货/流水阻断资格。</summary>
    public async Task<IReadOnlyList<SafePurchaseDraftCandidateDto>> GetSafeAppendCandidatesAsync(
        string sourceType, Guid sourceUid, IEnumerable<Guid> supplierIds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceType)) throw new ArgumentException("来源类型不能为空。", nameof(sourceType));
        if (sourceUid == Guid.Empty) throw new ArgumentException("来源 Uid 不能为空。", nameof(sourceUid));
        var ids = supplierIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return [];
        var values = new PostgreSqlParameters();
        values.AddIds("SupplierIds", ids);
        values.Add("ActiveLifecycle", (int)PurchaseOrderLifecycleStatusEnum.Active);
        values.Add("PendingConfirmation", (int)ConfirmationStatusEnum.待确认);
        var sql = $"""
            SELECT h."uid" AS "PurchaseOrderId",h."version" AS "Version",
                   h."order_no" AS "OrderNo",h."demand_source_type" AS "SourceType",
                   h."demand_source_uid" AS "SourceUid",h."supplier_id" AS "SupplierId",
                   COALESCE(amounts.untaxed,0) AS "UntaxedTotalAmount",
                   COALESCE(amounts.tax_included,0) AS "TaxIncludedTotalAmount"
            FROM "public"."purchase_order_head" h
            LEFT JOIN LATERAL
            (
                SELECT SUM(COALESCE(l."unit_amount",0)) AS untaxed,
                       SUM(COALESCE(l."tax_amount",0)) AS tax_included
                FROM "public"."purchase_order_line" l
                WHERE l."purchase_order_id"=h."uid" AND l."is_deleted"=FALSE
            ) amounts ON TRUE
            WHERE {Dictionary("h.\"demand_source_type\"")}=rtrim(@SourceType,' ') COLLATE public.erp_dictionary_ci
              AND h."demand_source_uid"=@SourceUid
              AND h."supplier_id"=ANY(@SupplierIds)
              AND h."lifecycle_status"=@ActiveLifecycle
              AND h."confirmation_status"=@PendingConfirmation
              AND h."is_deleted"=FALSE
              AND {SafeFacts()}
            ORDER BY {Dictionary("h.\"order_no\"")} ASC NULLS FIRST,{UuidOrder("h.\"uid\"")} ASC NULLS FIRST;
            """;
        var rows = await context.Session.Connection.QueryAsync<SafePurchaseDraftCandidateDto>(new CommandDefinition(
            sql, new SourceParameters(values, sourceType.Trim(), sourceUid), context.Session.Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    /// <summary>普通批量读取未删采购头的原44列；独立及各种状态保留，缺失省略，空集合不执行SQL。</summary>
    public async Task<IReadOnlyList<PurchaseOrderHead>> GetPurchaseDraftsByIdsAsync(
        IEnumerable<Guid> uids, CancellationToken cancellationToken = default)
    {
        var ids = uids.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return [];
        var sql = $"""
            SELECT {HeadProjection} FROM "public"."purchase_order_head" h
            WHERE h."uid"=ANY(@Uids) AND h."is_deleted"=FALSE
            ORDER BY {Dictionary("h.\"demand_source_type\"")} ASC NULLS FIRST,
                     {UuidOrder("h.\"demand_source_uid\"")} ASC NULLS FIRST,
                     {UuidOrder("h.\"supplier_id\"")} ASC NULLS FIRST,
                     {UuidOrder("h.\"uid\"")} ASC NULLS FIRST;
            """;
        var rows = await context.Session.Connection.QueryAsync<PurchaseOrderHead>(new CommandDefinition(
            sql, PostgreSqlParameters.Ids(ids), context.Session.Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    /// <summary>按原执行请求、来源和供应商读取并锁定实际未删采购头；保留入口校验和Trim，缺失范围不虚构保护。</summary>
    public Task<PurchaseOrderHead?> GetByExecutionScopeForUpdateAsync(
        Guid executionRequestId, string sourceType, Guid sourceUid, Guid supplierId)
    {
        if (executionRequestId == Guid.Empty) throw new ArgumentException("执行请求 Uid 不能为空。", nameof(executionRequestId));
        if (string.IsNullOrWhiteSpace(sourceType)) throw new ArgumentException("来源类型不能为空。", nameof(sourceType));
        if (sourceUid == Guid.Empty) throw new ArgumentException("来源 Uid 不能为空。", nameof(sourceUid));
        if (supplierId == Guid.Empty) throw new ArgumentException("供应商 Uid 不能为空。", nameof(supplierId));
        var values = new PostgreSqlParameters();
        values.Add("ExecutionRequestId", executionRequestId);
        values.Add("SupplierId", supplierId);
        return context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>($"""
            SELECT h."uid" AS "Uid",h."version" AS "Version",h."is_deleted" AS "IsDeleted",h."created_date" AS "CreatedDate",h."created_by" AS "CreatedBy",h."updated_date" AS "UpdatedDate",h."updated_by" AS "UpdatedBy",h."confirmation_status" AS "ConfirmationStatus",h."lifecycle_status" AS "LifecycleStatus",h."order_no" AS "OrderNo",h."bo_no" AS "BoNo",h."customer_po_no" AS "CustomerPoNo",h."customer_id" AS "CustomerId",h."customer_code" AS "CustomerCode",h."customer_name_snapshot" AS "CustomerNameSnapshot",h."currency" AS "Currency",h."discount_rate" AS "DiscountRate",h."discount_amount" AS "DiscountAmount",h."supplier_id" AS "SupplierId",h."supplier_code" AS "SupplierCode",h."supplier_name" AS "SupplierName",h."demand_source_type" AS "DemandSourceType",h."demand_source_uid" AS "DemandSourceUid",h."demand_source_no" AS "DemandSourceNo",h."execution_request_id" AS "ExecutionRequestId",h."so_person" AS "SoPerson",h."po_person" AS "PoPerson",h."inletr_hose_type" AS "InletrHoseType",h."aerator_type" AS "AeratorType",h."pallet_type" AS "PalletType",h."box_type" AS "BoxType",h."label_type" AS "LabelType",h."im_type" AS "IMType",h."carton_type" AS "CartonType",h."logo_type" AS "LogoType",h."reviewed_by" AS "ReviewedBy",h."po_folder" AS "PoFolder",h."zhe_kou_bei_zhu" AS "ZheKouBeiZhu",h."remark" AS "Remark",h."order_date" AS "OrderDate",h."expected_date" AS "ExpectedDate",h."reply_date" AS "ReplyDate",h."confirmed_date" AS "ConfirmedDate",h."confirmed_by" AS "ConfirmedBy" FROM "public"."purchase_order_head" h
            WHERE h."execution_request_id"=@ExecutionRequestId
                AND {Dictionary("h.\"demand_source_type\"")}=rtrim(@SourceType,' ') COLLATE public.erp_dictionary_ci
                AND h."demand_source_uid"=@SourceUid AND h."supplier_id"=@SupplierId
                AND h."is_deleted"=FALSE FOR UPDATE OF h;
            """, new SourceParameters(values, sourceType.Trim(), sourceUid), context.Session.Transaction);
    }

    /// <summary>按原版本、活动待确认及安全事实资格锁定实际头；保留非空执行身份要求，不补OriginKind或其他业务资格。</summary>
    public Task<PurchaseOrderHead?> GetSafeDraftForUpdateAsync(Guid uid, int expectedVersion)
    {
        ValidateHeadKey(uid, expectedVersion);
        return context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>($"""
            SELECT h."uid" AS "Uid",h."version" AS "Version",h."is_deleted" AS "IsDeleted",h."created_date" AS "CreatedDate",h."created_by" AS "CreatedBy",h."updated_date" AS "UpdatedDate",h."updated_by" AS "UpdatedBy",h."confirmation_status" AS "ConfirmationStatus",h."lifecycle_status" AS "LifecycleStatus",h."order_no" AS "OrderNo",h."bo_no" AS "BoNo",h."customer_po_no" AS "CustomerPoNo",h."customer_id" AS "CustomerId",h."customer_code" AS "CustomerCode",h."customer_name_snapshot" AS "CustomerNameSnapshot",h."currency" AS "Currency",h."discount_rate" AS "DiscountRate",h."discount_amount" AS "DiscountAmount",h."supplier_id" AS "SupplierId",h."supplier_code" AS "SupplierCode",h."supplier_name" AS "SupplierName",h."demand_source_type" AS "DemandSourceType",h."demand_source_uid" AS "DemandSourceUid",h."demand_source_no" AS "DemandSourceNo",h."execution_request_id" AS "ExecutionRequestId",h."so_person" AS "SoPerson",h."po_person" AS "PoPerson",h."inletr_hose_type" AS "InletrHoseType",h."aerator_type" AS "AeratorType",h."pallet_type" AS "PalletType",h."box_type" AS "BoxType",h."label_type" AS "LabelType",h."im_type" AS "IMType",h."carton_type" AS "CartonType",h."logo_type" AS "LogoType",h."reviewed_by" AS "ReviewedBy",h."po_folder" AS "PoFolder",h."zhe_kou_bei_zhu" AS "ZheKouBeiZhu",h."remark" AS "Remark",h."order_date" AS "OrderDate",h."expected_date" AS "ExpectedDate",h."reply_date" AS "ReplyDate",h."confirmed_date" AS "ConfirmedDate",h."confirmed_by" AS "ConfirmedBy" FROM "public"."purchase_order_head" h
            WHERE h."uid"=@Uid AND h."version"=@ExpectedVersion
                AND h."lifecycle_status"=@ActiveLifecycle AND h."confirmation_status"=@PendingConfirmation
                AND {Dictionary("h.\"demand_source_type\"")}<>''
                AND h."demand_source_uid"<>@EmptyUid AND h."supplier_id"<>@EmptyUid
                AND h."execution_request_id"<>@EmptyUid AND h."is_deleted"=FALSE
                AND {SafeFactsWithLocks()} FOR UPDATE OF h;
            """, new
        {
            Uid = uid,
            ExpectedVersion = expectedVersion,
            EmptyUid = Guid.Empty,
            ActiveLifecycle = (int)PurchaseOrderLifecycleStatusEnum.Active,
            PendingConfirmation = (int)ConfirmationStatusEnum.待确认
        }, context.Session.Transaction);
    }

    /// <summary>按原45固定头字段新增；保留Application生成的Uid，打戳后清软删，仅非正版本调整为1，返回影响行数。</summary>
    public async Task<int> InsertPurchaseDraftAsync(PurchaseOrderHead head)
    {
        ArgumentNullException.ThrowIfNull(head);
        if (head.Uid == Guid.Empty) throw new ArgumentException("采购草稿必须由 Application 预生成 Uid。", nameof(head));
        context.AuditStamp.StampForInsert(head);
        head.IsDeleted = false;
        if (head.Version <= 0) head.Version = 1;
        return await context.Session.Connection.ExecuteAsync(
            HeadInsertSql,
            PostgreSqlParameters.Entity(head, Head), context.Session.Transaction);
    }

    /// <summary>锁现有未删明细后聚合最大行号；父头未删才计入，空集返回0，不宣称明细空范围已加锁。</summary>
    public Task<int> GetMaxLineNumberForUpdateAsync(Guid purchaseOrderId)
    {
        if (purchaseOrderId == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(purchaseOrderId));
        return context.Session.Connection.ExecuteScalarAsync<int>($"""
            WITH locked_lines AS MATERIALIZED (
                SELECT l."line_number" AS line_number FROM "public"."purchase_order_line" l
                WHERE l."purchase_order_id"=@PurchaseOrderId AND l."is_deleted"=FALSE
                    AND EXISTS (SELECT 1 FROM "public"."purchase_order_head" owner WHERE owner."uid"=@PurchaseOrderId
                        AND owner."is_deleted"=FALSE)
                FOR UPDATE OF l
            ) SELECT COALESCE(MAX(line_number),0) FROM locked_lines;
            """, new { PurchaseOrderId = purchaseOrderId }, context.Session.Transaction);
    }

    /// <summary>先物化和校验全部行身份，再统一打戳；按输入顺序锁现有父头并插入原固定列，缺父头立即失败，由外层事务回滚。</summary>
    public async Task<int> InsertPurchaseDraftLinesAsync(IEnumerable<PurchaseOrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var materialized = lines.ToArray();
        if (materialized.Length == 0) return 0;
        if (materialized.Any(x => x is null || x.Uid == Guid.Empty))
            throw new ArgumentException("采购草稿行必须由 Application 预生成 Uid。", nameof(lines));
        foreach (var line in materialized)
        {
            context.AuditStamp.StampForInsert(line);
            line.IsDeleted = false;
            if (line.Version <= 0) line.Version = 1;
        }
        // 单语句将父头锁与INSERT关联，避免C#先查头后插入之间引入新窗口；空owner不写行，随后抛原中文失败。
        var sql = $"""
            WITH owner AS MATERIALIZED (
                SELECT h."uid" FROM "public"."purchase_order_head" h
                WHERE h."uid"=@PurchaseOrderId AND h."is_deleted"=FALSE FOR UPDATE OF h
            )
            INSERT INTO "public"."purchase_order_line" ("uid","version","is_deleted","created_date","created_by","updated_date","updated_by","purchase_order_id","line_number","order_no","product_id","sku","customer_sku","link_sku","supplier_sku","description_zh","finish_zh","features_zh","unit_snapshot","quantity","unlinked_qty","unit_price","tax_price","unit_amount","tax_amount","planned_delivery_date","delivery_target_supplier_id","delivery_supplier_code_snapshot","delivery_supplier_name_snapshot","sub_note","supply_type","material_type","source_type","source_no","source_uid","source_line_id","source_line_number")
            SELECT @Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@PurchaseOrderId,@LineNumber,@OrderNo,@ProductId,@Sku,@CustomerSku,@LinkSku,@SupplierSku,@DescriptionZh,@FinishZh,@FeaturesZh,@UnitSnapshot,@Quantity,@UnlinkedQty,@UnitPrice,@TaxPrice,@UnitAmount,@TaxAmount,@PlannedDeliveryDate,@DeliveryTargetSupplierId,@DeliverySupplierCodeSnapshot,@DeliverySupplierNameSnapshot,@SubNote,@SupplyType,@MaterialType,@SourceType,@SourceNo,@SourceUid,@SourceLineId,@SourceLineNumber WHERE EXISTS (SELECT 1 FROM owner);
            """;
        var count = 0;
        foreach (var line in materialized)
        {
            var affected = await context.Session.Connection.ExecuteAsync(sql, PostgreSqlParameters.Entity(line, Line), context.Session.Transaction);
            if (affected == 0) throw new InvalidOperationException("采购单不存在或已删除，不能追加采购行。");
            count += affected;
        }
        return count;
    }

    /// <summary>按原乐观版本和安全门禁推进头版本，重算未删正数量行最早交期；直接写后台时间，不回写调用方头实体。</summary>
    public Task<int> AdvanceDraftVersionAfterAppendAsync(Guid uid, int expectedVersion)
    {
        ValidateHeadKey(uid, expectedVersion);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Uid", uid);
        parameters.Add("ExpectedVersion", expectedVersion);
        parameters.Add("Active", (int)PurchaseOrderLifecycleStatusEnum.Active);
        parameters.Add("Pending", (int)ConfirmationStatusEnum.待确认);
        parameters.AddTimestamp("Now", DateTime.Now);
        parameters.Add("UserName", context.CurrentUser.UserName);
        // 锁定实际合格头及实际正数量行后才MIN，避免对聚合结果非法FOR UPDATE；缺行/新收货范围仍由联合协议验收。
        return context.Session.Connection.ExecuteAsync($"""
            WITH candidate AS MATERIALIZED (
                SELECT h."uid" AS uid FROM "public"."purchase_order_head" h
                WHERE h."uid"=@Uid AND h."version"=@ExpectedVersion
                    AND h."lifecycle_status"=@Active AND h."confirmation_status"=@Pending
                    AND h."is_deleted"=FALSE AND {SafeFactsWithLocks()} FOR UPDATE OF h
            ), locked_dates AS MATERIALIZED (
                SELECT l."planned_delivery_date" AS planned_date FROM "public"."purchase_order_line" l
                JOIN candidate owner ON owner.uid=l."purchase_order_id"
                WHERE l."is_deleted"=FALSE AND l."quantity">0 FOR UPDATE OF l
            ), facts AS (SELECT MIN(planned_date) AS earliest_date FROM locked_dates)
            UPDATE "public"."purchase_order_head" h SET "expected_date"=facts.earliest_date,
                "updated_date"=@Now,"updated_by"=@UserName,
                "version"=h."version"+1
            FROM candidate owner CROSS JOIN facts
            WHERE h."uid"=owner.uid AND h."version"=@ExpectedVersion;
            """, parameters, context.Session.Transaction);
    }

    // 新增固定写列保留原范围；PG quantity 对应实体 Quantity，读取范围不因本次整理扩展。

    /// <summary>沿原头身份和乐观版本校验顺序，在SQL前拒绝空身份或非正版本。</summary>
    private static void ValidateHeadKey(Guid uid, int expectedVersion)
    {
        if (uid == Guid.Empty) throw new ArgumentException("采购头 Uid 不能为空。", nameof(uid));
        if (expectedVersion <= 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
    }

    /// <summary>保留原入库/流水门禁及软删采购行范围，锁实际匹配行和关联采购行；不存在的下游范围不虚构保护。</summary>
    private static string SafeFactsWithLocks() => $"""
        NOT EXISTS (
            SELECT 1 FROM "public"."inbound_document_line" r
            WHERE COALESCE(r."is_deleted",FALSE)=FALSE AND r."source_line_id" IN
                (SELECT p."uid" FROM "public"."purchase_order_line" p WHERE p."purchase_order_id"=h."uid" FOR UPDATE OF p)
            FOR UPDATE OF r
        ) AND NOT EXISTS (
            SELECT 1 FROM "public"."inventory_ledger_entry" e JOIN "public"."inbound_document_line" history ON history."uid"=e."source_line_id"
            WHERE history."source_line_id" IN
                (SELECT p."uid" FROM "public"."purchase_order_line" p WHERE p."purchase_order_id"=h."uid" FOR UPDATE OF p)
            FOR UPDATE OF e,history
        )
        """;

    /// <summary>保留全部采购行身份与收货历史；软删采购行、软删中间入库及反向流水均不能隐藏库存事实。</summary>
    private static string SafeFacts() => $"""
        NOT EXISTS
        (
            SELECT 1 FROM "public"."inbound_document_line" r
            WHERE COALESCE(r."is_deleted",FALSE)=FALSE
              AND r."source_line_id" IN
                  (SELECT p."uid" FROM "public"."purchase_order_line" p WHERE p."purchase_order_id"=h."uid")
        )
        AND NOT EXISTS
        (
            SELECT 1 FROM "public"."inventory_ledger_entry" e
            JOIN "public"."inbound_document_line" history ON history."uid"=e."source_line_id"
            WHERE history."source_line_id" IN
                  (SELECT p."uid" FROM "public"."purchase_order_line" p WHERE p."purchase_order_id"=h."uid")
        )
        """;
    /// <summary>沿当前已批准尾ASCII空格及ICU口径，广域来源等值仍由主控专项验收。</summary>
    private static string Dictionary(string column) => $"rtrim({column},' ') COLLATE public.erp_dictionary_ci";
    /// <summary>复现SqlGuid比较权重；nullable来源UUID先返回SQL NULL，避免全NULL数组排到真实UUID之后。</summary>
    private static string UuidOrder(string column) => $"CASE WHEN {column} IS NULL THEN NULL ELSE ARRAY[" +
        string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(x => $"get_byte(uuid_send({column}),{x})")) + "] END";

    /// <summary>为来源文本和UUID补充明确类型，其余标量和UUID数组复用已有参数包及克隆规则。</summary>
    private sealed class SourceParameters(PostgreSqlParameters values, string sourceType, Guid sourceUid) : SqlMapper.IDynamicParameters
    {
        /// <summary>绑定原Trim后的文本；不修改存储值，不接管连接、事务或取消。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            values.AddParameters(command, identity);
            command.Parameters.Add(new NpgsqlParameter("SourceType", NpgsqlDbType.Text) { Value = sourceType });
            command.Parameters.Add(new NpgsqlParameter("SourceUid", NpgsqlDbType.Uuid) { Value = sourceUid });
        }
    }
}
