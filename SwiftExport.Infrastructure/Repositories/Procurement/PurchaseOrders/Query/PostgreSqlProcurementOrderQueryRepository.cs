using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;

/// <summary>采购订单基础PG查询；分别观察count/真实页及头汇总/当前行，不接DI或其他采购事实。</summary>
public sealed class PostgreSqlProcurementOrderQueryRepository(RepositoryBaseContext context) : IProcurementOrderQueryRepository
{
    private static readonly EntityMetadata ProductMap = EntityMetadataCache.GetMetadata(typeof(Product));

    /// <summary>按源参数先取得独立总数再读取当前商业页；分页由调用服务校验，仓储不新增默认值或事务。</summary>
    public async Task<PagedResponseBase<ProcurementOrderListItemDto>> SearchAsync(ProcurementOrderSearchRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var parameters = new PostgreSqlParameters();
        parameters.Add("CustomerId", request.CustomerId);
        parameters.Add("SupplierId", request.SupplierId);
        parameters.Add("ConfirmationStatus", request.ConfirmationStatus.HasValue ? (int)request.ConfirmationStatus.Value : null);
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null
            : "%" + request.Keyword.Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
        parameters.Add("Keyword", keyword);
        parameters.Add("Skip", (request.PageIndex - 1) * request.PageSize);
        parameters.Add("Take", request.PageSize);
        var filter = $"""
            FROM "public"."purchase_order_head" h WHERE h."is_deleted"=FALSE
            AND (@CustomerId IS NULL OR h."customer_id"=@CustomerId)
            AND (@SupplierId IS NULL OR h."supplier_id"=@SupplierId)
            AND (@ConfirmationStatus IS NULL OR h."confirmation_status"=@ConfirmationStatus)
            AND (@Keyword IS NULL OR h."order_no" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')
            """;
        var count = await context.Session.Connection.ExecuteScalarAsync<int>(Command("SELECT COUNT(*)::integer " + filter, parameters, ct));
        var items = (await context.Session.Connection.QueryAsync<ProcurementOrderListItemDto>(Command($"""
            SELECT h."uid" AS "PurchaseOrderId",
                h."order_no" AS "OrderNo",
                h."customer_id" AS "CustomerId",
                h."customer_code" AS "CustomerCode",
                h."customer_name_snapshot" AS "CustomerName",
                h."supplier_id" AS "SupplierId",
                h."supplier_code" AS "SupplierCode",
                h."supplier_name" AS "SupplierName",
                h."order_date" AS "OrderDate",
                h."confirmation_status" AS "ConfirmationStatus",
                h."lifecycle_status" AS "LifecycleStatus",
                h."version" AS "Version"
            {filter}
            ORDER BY h."order_date" DESC NULLS LAST,
                     h."order_no" COLLATE public.erp_dictionary_ci DESC NULLS LAST,
                     {SourceUuidOrder("h.\"uid\"")} DESC
            OFFSET @Skip LIMIT @Take
            """, parameters, ct))).ToList();
        return new() { TotalCount = count, Items = items, PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <summary>先读取头及当阶段总量，再完整缓冲当前有效行；空头也完成行阶段，商业NULL与源按钮优先级保持。</summary>
    public async Task<PurchaseOrderDetailDto?> GetDetailAsync(Guid uid, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var parameters = new PostgreSqlParameters();
        parameters.Add("Uid", uid);
        parameters.Add("Pending", (int)ConfirmationStatusEnum.待确认);
        parameters.Add("Confirmed", (int)ConfirmationStatusEnum.已确认);
        parameters.Add("Active", (int)PurchaseOrderLifecycleStatusEnum.Active);
        var head = await context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderDetailDto>(Command($"""
            SELECT h."uid" AS "PurchaseOrderId",
                h."order_no" AS "OrderNo",
                h."customer_id" AS "CustomerId",
                h."customer_code" AS "CustomerCode",
                h."customer_name_snapshot" AS "CustomerName",
                h."supplier_id" AS "SupplierId",
                h."supplier_code" AS "SupplierCode",
                h."supplier_name" AS "SupplierName",
                h."currency" AS "Currency",
                h."discount_rate" AS "DiscountRate",
                h."discount_amount" AS "DiscountAmount",
                h."bo_no" AS "BoNo",
                h."customer_po_no" AS "CustomerPoNo",
                h."demand_source_type" AS "DemandSourceType",
                h."demand_source_uid" AS "DemandSourceUid",
                h."demand_source_no" AS "DemandSourceNo",
                h."so_person" AS "SalesPerson",
                h."po_person" AS "PurchasePerson",
                h."inletr_hose_type" AS "InletHoseType",
                h."aerator_type" AS "AeratorType",
                h."pallet_type" AS "PalletType",
                h."box_type" AS "BoxType",
                h."label_type" AS "LabelType",
                h."im_type" AS "InstructionManualType",
                h."carton_type" AS "CartonType",
                h."logo_type" AS "LogoType",
                h."reviewed_by" AS "ReviewedBy",
                h."po_folder" AS "PurchaseOrderFolder",
                h."zhe_kou_bei_zhu" AS "DiscountRemark",
                h."order_date" AS "OrderDate",
                h."expected_date" AS "ExpectedDate",
                h."reply_date" AS "ReplyDate",
                h."confirmed_date" AS "ConfirmedDate",
                h."confirmed_by" AS "ConfirmedBy",
                h."remark" AS "Remark",
                h."packaging_requirements" AS "PackagingRequirements",
                h."version" AS "Version",
                h."confirmation_status" AS "ConfirmationStatus",
                h."lifecycle_status" AS "LifecycleStatus",
                h."active_confirmation_id" AS "ActiveConfirmationId",
                h."created_date" AS "CreatedDate",
                h."created_by" AS "CreatedBy",
                h."updated_date" AS "UpdatedDate",
                h."updated_by" AS "UpdatedBy",
                CASE WHEN h."confirmation_status"=@Pending AND h."lifecycle_status"=@Active THEN TRUE ELSE FALSE END AS "CanEditDraft",
                CASE WHEN h."confirmation_status"=@Pending AND h."lifecycle_status"=@Active THEN TRUE ELSE FALSE END AS "CanConfirm",
                CASE WHEN h."confirmation_status"=@Confirmed AND h."lifecycle_status"=@Active THEN TRUE ELSE FALSE END AS "CanCancelConfirmation",
                CASE WHEN h."lifecycle_status"<>@Active THEN '采购单不是活动状态。'
                     WHEN h."confirmation_status" NOT IN (@Pending,@Confirmed) THEN '采购确认状态不支持当前命令。'
                     ELSE '' END AS "CommandBlockedReason",
                totals.line_count AS "LineCount",totals.total_quantity AS "TotalQuantity",totals.cny_total_amount AS "CnyTotalAmount"
            FROM "public"."purchase_order_head" h
            LEFT JOIN LATERAL (
                SELECT COUNT(*)::integer AS line_count,COALESCE(SUM(l."quantity"),0) AS total_quantity,
                       COALESCE(SUM(l."unit_amount"),0) AS cny_total_amount
                FROM "public"."purchase_order_line" l WHERE l."purchase_order_id"=h."uid"
                    AND l."is_deleted"=FALSE
            ) totals ON TRUE
            WHERE h."uid"=@Uid AND h."is_deleted"=FALSE
            """, parameters, ct));
        // 源在空头时仍消费完整行结果；不可提前返回或刷新已经读出的头汇总。
        var lines = (await context.Session.Connection.QueryAsync<PurchaseOrderLineDetailDto>(Command($"""
            SELECT l."uid" AS "Uid",
                l."line_number" AS "LineNumber",
                l."version" AS "Version",
                l."product_id" AS "ProductId",
                l."sku" AS "Sku",
                l."unit_snapshot" AS "UnitSnapshot",
                l."customer_sku" AS "CustomerSku",
                l."link_sku" AS "LinkSku",
                l."finish_zh" AS "FinishZh",
                l."features_zh" AS "FeaturesZh",
                l."material_type" AS "MaterialType",
                l."supply_type" AS "SupplyType",
                l."quantity" AS "Quantity",
                l."unlinked_qty" AS "UnlinkedQty",
                l."source_type" AS "SourceType",
                l."source_no" AS "SourceNo",
                l."source_uid" AS "SourceUid",
                l."source_line_id" AS "SourceLineId",
                l."source_line_number" AS "SourceLineNumber",
                l."unit_price" AS "UnitPrice",
                l."tax_price" AS "TaxPrice",
                l."unit_amount" AS "UnitAmount",
                l."tax_amount" AS "TaxAmount",
                l."planned_delivery_date" AS "PlannedDeliveryDate",
                l."supplier_sku" AS "SupplierSku",
                l."description_zh" AS "DescriptionZh",
                l."sub_note" AS "SubNote",
                l."created_date" AS "CreatedDate",
                l."created_by" AS "CreatedBy",
                l."updated_date" AS "UpdatedDate",
                l."updated_by" AS "UpdatedBy",
                {P(nameof(Product.MainImage))} AS "MainImage",
                (l."product_id" IS NULL) AS "IsNonStandard",
                CASE WHEN rtrim(l."material_type",' ') COLLATE public.erp_dictionary_ci='客供件'
                     THEN l."delivery_target_supplier_id" END AS "DeliverySupplierId",
                CASE WHEN rtrim(l."material_type",' ') COLLATE public.erp_dictionary_ci='客供件'
                     THEN l."delivery_supplier_code_snapshot" ELSE '' END AS "DeliverySupplierCode",
                CASE WHEN rtrim(l."material_type",' ') COLLATE public.erp_dictionary_ci='客供件'
                     THEN l."delivery_supplier_name_snapshot" ELSE '' END AS "DeliverySupplierName"
            FROM "public"."purchase_order_line" l JOIN "public"."purchase_order_head" h ON h."uid"=l."purchase_order_id"
            LEFT JOIN "public"."product" p ON {P(nameof(Product.Uid))}=l."product_id" AND {P(nameof(Product.IsDeleted))}=FALSE
            WHERE h."uid"=@Uid AND h."is_deleted"=FALSE AND l."is_deleted"=FALSE
            ORDER BY l."line_number",{SourceUuidOrder("l.\"uid\"")}
            """, parameters, ct))).ToList();
        if (head is not null) head.Lines = lines;
        return head;
    }

    /// <summary>借用请求连接与可空事务，显式类型包只处理本文件命令，不更改公共参数或隔离。</summary>
    private CommandDefinition Command(string sql, PostgreSqlParameters parameters, CancellationToken token)
        => new(sql, new QueryParameters(parameters), context.Session.Transaction, cancellationToken: token);


    /// <summary>通过CLR属性取PG物理列；数量采用quantity，不沿用旧SQL Server的QTY名称。</summary>
    private static string Column(EntityMetadata map, string alias, string property)
        => alias + "." + PostgreSqlNames.Quote(map.Columns.Single(column => column.PropertyName == property).ColumnName);
    /// <summary>定位当前产品属性，不把主数据值覆盖采购商业快照。</summary>
    private static string P(string property) => Column(ProductMap, "p", property);

    /// <summary>保留源uniqueidentifier顺序，仅用于内部固定SQL的UUID排序键。</summary>
    private static string SourceUuidOrder(string column)
        => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(offset => $"get_byte(uuid_send({column}),{offset})")) + "]";

    /// <summary>在既有参数克隆上明确nullable标量类型，不让NULL参数依赖服务端或包装连接推断。</summary>
    private sealed class QueryParameters(PostgreSqlParameters parameters) : SqlMapper.IDynamicParameters
    {
        /// <summary>命令拥有独立参数实例；只明确源筛选/标识/状态的类型，不改变值或所有权。</summary>
        public void AddParameters(System.Data.IDbCommand command, SqlMapper.Identity identity)
        {
            parameters.AddParameters(command, identity);
            foreach (NpgsqlParameter parameter in command.Parameters)
                parameter.NpgsqlDbType = parameter.ParameterName switch
                {
                    "CustomerId" or "SupplierId" or "Uid" => NpgsqlDbType.Uuid,
                    "Keyword" => NpgsqlDbType.Text,
                    _ => NpgsqlDbType.Integer
                };
        }
    }
}
