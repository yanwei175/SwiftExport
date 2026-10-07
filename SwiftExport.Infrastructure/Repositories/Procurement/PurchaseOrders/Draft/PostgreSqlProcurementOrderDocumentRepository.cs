using Dapper;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;

/// <summary>在调用方 PG 事务内读取、创建并保存采购头行，保留原商务白名单和版本结果，不接管业务事务。</summary>
public sealed class PostgreSqlProcurementOrderDocumentRepository(RepositoryBaseContext context) : IProcurementOrderDocumentRepository
{
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string HeadInsertSql = """
        INSERT INTO "public"."purchase_order_head" ("uid",
        "version",
        "is_deleted",
        "created_date",
        "created_by",
        "updated_date",
        "updated_by",
        "origin_kind",
        "customer_id",
        "customer_code",
        "customer_name_snapshot",
        "supplier_id",
        "supplier_code",
        "supplier_name",
        "order_no",
        "currency",
        "discount_rate",
        "discount_amount",
        "order_date",
        "expected_date",
        "remark",
        "packaging_requirements",
        "execution_request_id",
        "demand_source_type",
        "demand_source_uid",
        "demand_source_no",
        "bo_no",
        "customer_po_no",
        "reply_date",
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
        "zhe_kou_bei_zhu",
        "confirmation_status",
        "lifecycle_status") VALUES (@Uid,
        @Version,
        @IsDeleted,
        @CreatedDate,
        @CreatedBy,
        @UpdatedDate,
        @UpdatedBy,
        @OriginKind,
        @CustomerId,
        @CustomerCode,
        @CustomerNameSnapshot,
        @SupplierId,
        @SupplierCode,
        @SupplierName,
        @OrderNo,
        @Currency,
        @DiscountRate,
        @DiscountAmount,
        @OrderDate,
        @ExpectedDate,
        @Remark,
        @PackagingRequirements,
        @ExecutionRequestId,
        NULL,
        NULL,
        NULL,
        @BoNo,
        @CustomerPoNo,
        @ReplyDate,
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
        @ZheKouBeiZhu,
        @ConfirmationStatus,
        @LifecycleStatus);
    """;
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string LineInsertSql = """
        INSERT INTO "public"."purchase_order_line" ("uid",
        "version",
        "is_deleted",
        "created_date",
        "created_by",
        "updated_date",
        "updated_by",
        "purchase_order_id",
        "line_number",
        "order_no",
        "product_id",
        "sku",
        "unit_snapshot",
        "quantity",
        "unlinked_qty",
        "supply_type",
        "material_type",
        "delivery_target_supplier_id",
        "delivery_supplier_code_snapshot",
        "delivery_supplier_name_snapshot",
        "unit_price",
        "tax_price",
        "unit_amount",
        "tax_amount",
        "planned_delivery_date",
        "supplier_sku",
        "description_zh",
        "sub_note",
        "customer_sku",
        "link_sku",
        "finish_zh",
        "features_zh",
        "source_type",
        "source_no") VALUES (@Uid,
        @Version,
        @IsDeleted,
        @CreatedDate,
        @CreatedBy,
        @UpdatedDate,
        @UpdatedBy,
        @PurchaseOrderId,
        @LineNumber,
        @OrderNo,
        @ProductId,
        @Sku,
        @UnitSnapshot,
        @Quantity,
        @UnlinkedQty,
        @SupplyType,
        @MaterialType,
        @DeliveryTargetSupplierId,
        @DeliverySupplierCodeSnapshot,
        @DeliverySupplierNameSnapshot,
        @UnitPrice,
        @TaxPrice,
        @UnitAmount,
        @TaxAmount,
        @PlannedDeliveryDate,
        @SupplierSku,
        @DescriptionZh,
        @SubNote,
        @CustomerSku,
        @LinkSku,
        @FinishZh,
        @FeaturesZh,
        NULL,
        NULL);
    """;
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string HeadAssignments = """
        "remark"=@Remark,
        "packaging_requirements"=@PackagingRequirements,
        "expected_date"=@ExpectedDate,
        "updated_date"=@UpdatedDate,
        "updated_by"=@UpdatedBy
    """;
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string LineAssignments = """
        "supplier_sku"=@SupplierSku,
        "description_zh"=@DescriptionZh,
        "sub_note"=@SubNote,
        "unit_price"=@UnitPrice,
        "tax_price"=@TaxPrice,
        "unit_amount"=@UnitAmount,
        "tax_amount"=@TaxAmount,
        "planned_delivery_date"=@PlannedDeliveryDate,
        "updated_date"=@UpdatedDate,
        "updated_by"=@UpdatedBy
    """;
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string ConfirmationAssignments = """
        "active_confirmation_id"=@ActiveConfirmationId,
        "confirmed_date"=@ConfirmedDate,
        "confirmed_by"=@ConfirmedBy,
        "updated_date"=@UpdatedDate,
        "updated_by"=@UpdatedBy
    """;

    private static readonly EntityMetadata Head = EntityMetadataCache.GetMetadata(typeof(PurchaseOrderHead));
    private static readonly EntityMetadata Line = EntityMetadataCache.GetMetadata(typeof(PurchaseOrderLine));

    /// <summary>读取未删采购头并锁定已有行；不存在身份的并发创建仍须由实际命令及唯一约束共同验收。</summary>
    public Task<PurchaseOrderHead?> GetHeadAsync(Guid uid, CancellationToken ct) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>(Command($"""
            SELECT h."uid" AS "Uid",h."version" AS "Version",h."is_deleted" AS "IsDeleted",h."created_date" AS "CreatedDate",h."created_by" AS "CreatedBy",h."updated_date" AS "UpdatedDate",h."updated_by" AS "UpdatedBy",h."origin_kind" AS "OriginKind",h."customer_id" AS "CustomerId",h."customer_name_snapshot" AS "CustomerNameSnapshot",h."active_confirmation_id" AS "ActiveConfirmationId",h."packaging_requirements" AS "PackagingRequirements",h."confirmation_status" AS "ConfirmationStatus",h."lifecycle_status" AS "LifecycleStatus",h."order_no" AS "OrderNo",h."bo_no" AS "BoNo",h."customer_po_no" AS "CustomerPoNo",h."customer_code" AS "CustomerCode",h."currency" AS "Currency",h."discount_rate" AS "DiscountRate",h."discount_amount" AS "DiscountAmount",h."supplier_id" AS "SupplierId",h."supplier_code" AS "SupplierCode",h."supplier_name" AS "SupplierName",h."demand_source_type" AS "DemandSourceType",h."demand_source_uid" AS "DemandSourceUid",h."demand_source_no" AS "DemandSourceNo",h."execution_request_id" AS "ExecutionRequestId",h."so_person" AS "SoPerson",h."po_person" AS "PoPerson",h."inletr_hose_type" AS "InletrHoseType",h."aerator_type" AS "AeratorType",h."pallet_type" AS "PalletType",h."box_type" AS "BoxType",h."label_type" AS "LabelType",h."im_type" AS "IMType",h."carton_type" AS "CartonType",h."logo_type" AS "LogoType",h."reviewed_by" AS "ReviewedBy",h."po_folder" AS "PoFolder",h."zhe_kou_bei_zhu" AS "ZheKouBeiZhu",h."remark" AS "Remark",h."order_date" AS "OrderDate",h."expected_date" AS "ExpectedDate",h."reply_date" AS "ReplyDate",h."confirmed_date" AS "ConfirmedDate",h."confirmed_by" AS "ConfirmedBy" FROM "public"."purchase_order_head" h
            WHERE h."uid"=@uid AND h."is_deleted"=FALSE FOR UPDATE OF h;
            """, new { uid }, ct));

    /// <summary>先锁有效父头，再读取并锁定已有有效明细；原行号及 SqlGuid 顺序不变，不假称 FOR UPDATE 能锁空范围。</summary>
    public async Task<List<PurchaseOrderLine>> GetLinesAsync(Guid uid, CancellationToken ct)
    {
        // 现用草稿保存和确认先持头锁；重复取得父头只作为同一单据已有明细集合的锚点，不新增全局锁。
        await context.Session.Connection.ExecuteAsync(Command($"""
            SELECT h."uid" FROM "public"."purchase_order_head" h
            WHERE h."uid"=@uid AND h."is_deleted"=FALSE FOR UPDATE OF h;
            """, new { uid }, ct));
        var rows = await context.Session.Connection.QueryAsync<PurchaseOrderLine>(Command($"""
            SELECT l."uid" AS "Uid",l."version" AS "Version",l."is_deleted" AS "IsDeleted",l."created_date" AS "CreatedDate",l."created_by" AS "CreatedBy",l."updated_date" AS "UpdatedDate",l."updated_by" AS "UpdatedBy",l."unlinked_qty" AS "UnlinkedQty",l."purchase_order_id" AS "PurchaseOrderId",l."line_number" AS "LineNumber",l."order_no" AS "OrderNo",l."product_id" AS "ProductId",l."sku" AS "Sku",l."customer_sku" AS "CustomerSku",l."link_sku" AS "LinkSku",l."supplier_sku" AS "SupplierSku",l."description_zh" AS "DescriptionZh",l."finish_zh" AS "FinishZh",l."features_zh" AS "FeaturesZh",l."unit_snapshot" AS "UnitSnapshot",l."quantity" AS "Quantity",l."unit_price" AS "UnitPrice",l."tax_price" AS "TaxPrice",l."unit_amount" AS "UnitAmount",l."tax_amount" AS "TaxAmount",l."planned_delivery_date" AS "PlannedDeliveryDate",l."delivery_target_supplier_id" AS "DeliveryTargetSupplierId",l."delivery_supplier_code_snapshot" AS "DeliverySupplierCodeSnapshot",l."delivery_supplier_name_snapshot" AS "DeliverySupplierNameSnapshot",l."sub_note" AS "SubNote",l."supply_type" AS "SupplyType",l."material_type" AS "MaterialType",l."source_type" AS "SourceType",l."source_no" AS "SourceNo",l."source_uid" AS "SourceUid",l."source_line_id" AS "SourceLineId",l."source_line_number" AS "SourceLineNumber" FROM "public"."purchase_order_line" l
            JOIN "public"."purchase_order_head" h ON h."uid"=l."purchase_order_id" AND h."is_deleted"=FALSE
            WHERE l."purchase_order_id"=@uid AND l."is_deleted"=FALSE
            ORDER BY l."line_number",{UuidOrder("l.\"uid\"")} FOR UPDATE OF l;
            """, new { uid }, ct));
        return rows.AsList();
    }

    /// <summary>按源独立采购列清单插入头行，保留调用方身份和版本；来源头三列与行两列仍强制写空。</summary>
    public async Task InsertAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, CancellationToken ct)
    {
        context.AuditStamp.StampForInsert(head);
        await context.Session.Connection.ExecuteAsync(Command(HeadInsertSql, PostgreSqlParameters.Entity(head, Head), ct));
        foreach (var line in lines)
        {
            context.AuditStamp.StampForInsert(line);
            await context.Session.Connection.ExecuteAsync(Command(LineInsertSql, PostgreSqlParameters.Entity(line, Line), ct));
        }
    }

    /// <summary>仅保存原头行商务字段；任一原版本未命中返回相同失败，由调用方回滚，不自行重试或推进实体版本。</summary>
    public async Task<RepositoryWriteResult> SaveAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, CancellationToken ct)
    {
        context.AuditStamp.StampForUpdate(head);
        var headSql = $"""
            UPDATE "public"."purchase_order_head" h SET {HeadAssignments},
                "version"=h."version"+1
            WHERE h."uid"=@Uid AND h."version"=@Version
                AND h."confirmation_status"=0 AND h."lifecycle_status"=0 AND h."is_deleted"=FALSE;
            """;
        if (await context.Session.Connection.ExecuteAsync(Command(headSql, PostgreSqlParameters.Entity(head, Head), ct)) != 1)
            return RepositoryWriteResult.Fail(head.Uid, head.Version, "CONCURRENCY_CONFLICT", "采购头并发冲突。");
        foreach (var line in lines)
        {
            context.AuditStamp.StampForUpdate(line);
            var lineSql = $"""
                UPDATE "public"."purchase_order_line" l SET {LineAssignments},
                    "version"=l."version"+1
                FROM "public"."purchase_order_head" h WHERE h."uid"=l."purchase_order_id"
                    AND l."uid"=@Uid AND l."purchase_order_id"=@PurchaseOrderId
                    AND l."version"=@Version AND l."is_deleted"=FALSE
                    AND h."is_deleted"=FALSE AND h."confirmation_status"=0 AND h."lifecycle_status"=0;
                """;
            if (await context.Session.Connection.ExecuteAsync(Command(lineSql, PostgreSqlParameters.Entity(line, Line), ct)) != 1)
                return RepositoryWriteResult.Fail(line.Uid, line.Version, "CONCURRENCY_CONFLICT", "采购行并发冲突。");
        }
        return RepositoryWriteResult.Ok(head.Uid, head.Version, head.Version + 1);
    }

    /// <summary>保存原确认状态、批次和审计字段；保持源状态与版本谓词及结构化失败，不补建确认组成。</summary>
    public async Task<RepositoryWriteResult> ConfirmAsync(PurchaseOrderHead head, CancellationToken ct)
    {
        context.AuditStamp.StampForUpdate(head);
        var sql = $"""
            UPDATE "public"."purchase_order_head" h SET "confirmation_status"=1,
                {ConfirmationAssignments},
                "version"=h."version"+1
            WHERE h."uid"=@Uid AND h."version"=@Version
                AND h."confirmation_status"=0 AND h."lifecycle_status"=0 AND h."is_deleted"=FALSE;
            """;
        if (await context.Session.Connection.ExecuteAsync(Command(sql, PostgreSqlParameters.Entity(head, Head), ct)) != 1)
            return RepositoryWriteResult.Fail(head.Uid, head.Version, "CONCURRENCY_CONFLICT", "采购确认并发冲突。");
        return RepositoryWriteResult.Ok(head.Uid, head.Version, head.Version + 1);
    }

    /// <summary>沿原采购持久化边界要求已有事务，透传取消令牌，不创建连接或事务。</summary>
    private CommandDefinition Command(string sql, object args, CancellationToken ct) => new(sql, args, context.Session.Transaction ?? throw new InvalidOperationException("采购持久化需要事务。"), cancellationToken: ct);

    /// <summary>维持源 uniqueidentifier 排序权重。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(index => $"get_byte(uuid_send({column}),{index})")) + "]";
}
