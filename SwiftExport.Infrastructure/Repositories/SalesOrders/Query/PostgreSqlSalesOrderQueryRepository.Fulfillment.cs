using System.Text.Json;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Procurement;

namespace SwiftExport.Infrastructure.Repositories.SalesOrders.Query;

/// <summary>销售履约切片保持来源、采购反馈和装箱历史各自的观察阶段与事实粒度。</summary>
public sealed partial class PostgreSqlSalesOrderQueryRepository
{
    private static readonly string FulfillmentCustomers = Table(typeof(Customer));
    private static readonly string FulfillmentProducts = Table(typeof(Product));
    private static readonly string FulfillmentNets = Table(typeof(NetDemand));
    private static readonly string FulfillmentLinks = Table(typeof(DemandExecutionLink));
    private static readonly string FulfillmentPurchaseHeads = Table(typeof(PurchaseOrderHead));
    private static readonly string FulfillmentPurchaseLines = Table(typeof(PurchaseOrderLine));
    private static readonly string FulfillmentReports = Table(typeof(SupplierProgressReport));

    /// <summary>逐阶段读取销售履约、整采购行反馈及箱单历史；只借用会话与可空事务，不接管连接或事务。</summary>
    public async Task<SalesFulfillmentDetailDto?> GetFulfillmentAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        var parameters = CreateParameters(uid);
        parameters.Add("NetActive", (int)NetDemandLifecycleStatusEnum.Active);
        parameters.Add("LinkActive", (int)DemandExecutionLinkStatusEnum.Active);
        parameters.Add("PurchaseVoided", (int)PurchaseOrderLifecycleStatusEnum.Voided);
        await FreezeCandidatesAsync(parameters, cancellationToken);
        await FreezeLineFactsAsync(parameters, cancellationToken);
        var headers = await ReadAsync<SalesFulfillmentDetailDto>(CreateFulfillmentHeaderSql(), parameters, cancellationToken);
        // 源空头也已执行第二结果之前的履约物化；Gross必须在这里重新选择，而不是复用行事实选中身份。
        var lines = await ReadAsync<SalesFulfillmentLineDto>(CreateFulfillmentLinesSql(), parameters, cancellationToken);
        var detail = headers.SingleOrDefault();
        if (detail is null) return null;
        detail.Lines = lines;
        var lineById = lines.ToDictionary(line => line.SalesOrderLineId);
        // 只传源临时关系的十六列，商业数量仍由SQL计算，DTO的采购/历史集合不进入关系。
        parameters.Add("FulfillmentLines", JsonSerializer.Serialize(lines.Select(line => new
        {
            line.SalesOrderLineId, line.LineNumber, line.Sku, line.CustomerSku, line.OrderedQty, line.UnitSnapshot,
            line.EffectiveDueDate, line.GrossDemandId, line.GrossDemandQty, line.GrossDemandUnitSnapshot,
            line.GrossDemandVersion, line.SourceState, line.ShippedQty, line.UnshippedQty,
            line.AllowedFurtherDeliveryQty, line.ReviewReason
        })));
        foreach (var row in await ReadAsync<SalesFulfillmentPurchaseDto>(CreateFulfillmentPurchasesSql(), parameters, cancellationToken))
            if (lineById.TryGetValue(row.SalesOrderLineId, out var line)) line.Purchases.Add(row);
        foreach (var row in await ReadAsync<SalesFulfillmentDeliveryDto>(CreateFulfillmentDeliveriesSql(), parameters, cancellationToken))
            if (lineById.TryGetValue(row.SalesOrderLineId, out var line)) line.Deliveries.Add(row);
        var invalidLinks = (await ReadAsync<Guid>(CreateFulfillmentInvalidLinksSql(), parameters, cancellationToken)).ToHashSet();
        foreach (var line in lines)
        {
            if (invalidLinks.Contains(line.SalesOrderLineId)) line.PurchaseReviewReason = "采购关联证据异常";
            line.PurchaseState = line.SourceState == "需核实" ? "未知／需核实"
                : invalidLinks.Contains(line.SalesOrderLineId)
                    || line.Purchases.Any(row => !string.IsNullOrEmpty(row.ReviewReason)) ? "需核实"
                : line.Purchases.Count == 0 ? "未关联" : "已关联";
        }
        return detail;
    }

    /// <summary>候选身份已冻结，真实头与客户在本阶段重新读取；源履约不再次过滤头软删除。</summary>
    private static string CreateFulfillmentHeaderSql() => $"""
        WITH candidate AS ({CandidateRelation})
        SELECT h.uid AS "SalesOrderId",h.order_no AS "OrderNo",COALESCE(h.customer_code,'') AS "CustomerCode",
               COALESCE(c.customer_name,'') AS "CustomerName",COALESCE(h.invoice_no,'') AS "InvoiceNo",
               h.document_status AS "DocumentStatus",h.confirmation_status AS "ConfirmationStatus",
               h.order_date AS "OrderDate",h.delivery_date AS "DeliveryDate"
        FROM candidate JOIN {Headers} h ON h.uid=candidate.uid
        LEFT JOIN {FulfillmentCustomers} c ON c.uid=h.customer_id AND c.is_deleted=FALSE
        """;

    /// <summary>冻结事实联当前行及第二次Gross选择；超发继续暴露来源历史，未知量与关闭零保持区别。</summary>
    private static string CreateFulfillmentLinesSql() => $"""
        WITH candidate AS ({CandidateRelation}),facts AS ({LineFactsRelation})
        SELECT f."SalesOrderLineId",f."LineNumber",COALESCE(l.sku,'') AS "Sku",
               COALESCE(l.customer_sku,'') AS "CustomerSku",f."Qty" AS "OrderedQty",
               COALESCE(f."UnitSnapshot",'') AS "UnitSnapshot",f."EffectiveDueDate",
               CASE WHEN f."ReviewReason" IS NULL OR NOT ({Different("f.\"ReviewReason\"", "'超发需核实'")}) THEN g.uid END AS "GrossDemandId",
               CASE WHEN f."ReviewReason" IS NULL OR NOT ({Different("f.\"ReviewReason\"", "'超发需核实'")}) THEN g.demand_qty END AS "GrossDemandQty",
               CASE WHEN f."ReviewReason" IS NULL OR NOT ({Different("f.\"ReviewReason\"", "'超发需核实'")}) THEN g.unit_snapshot END AS "GrossDemandUnitSnapshot",
               CASE WHEN f."ReviewReason" IS NULL OR NOT ({Different("f.\"ReviewReason\"", "'超发需核实'")}) THEN g.version END AS "GrossDemandVersion",
               CASE WHEN f."ReviewReason" IS NOT NULL AND {Different("f.\"ReviewReason\"", "'超发需核实'")} THEN '需核实'
                    WHEN g.uid IS NULL AND h.confirmation_status=@Pending THEN '待确认'
                    WHEN g.uid IS NULL THEN '未关联' ELSE '已关联' END AS "SourceState",
               f."ShippedQty",f."UnshippedQty",
               CASE WHEN f."UnshippedQty" IS NULL THEN NULL WHEN h.document_status=@OrderClosed THEN 0::numeric(18,4)
                    ELSE f."UnshippedQty" END AS "AllowedFurtherDeliveryQty",COALESCE(f."ReviewReason",'') AS "ReviewReason"
        FROM facts f JOIN {Lines} l ON l.uid=f."SalesOrderLineId"
        JOIN candidate h ON h.uid=f."SalesOrderId"
        LEFT JOIN LATERAL (
            SELECT demand.uid,demand.demand_qty,demand.unit_snapshot,demand.version
            FROM {Gross} demand
            WHERE NOT ({Different("demand.source_type", "@SalesSourceType")}) AND demand.source_uid=h.uid
              AND demand.source_line_id=l.uid AND demand.is_deleted=FALSE
              AND demand.lifecycle_status IN (@DemandActive,@DemandVoided)
            ORDER BY CASE WHEN demand.lifecycle_status=@DemandActive THEN 0 ELSE 1 END,
                     demand.source_version DESC NULLS LAST,{SourceUuidOrder("demand.uid")} LIMIT 1
        ) g ON TRUE
        ORDER BY f."LineNumber",{SourceUuidOrder("f.\"SalesOrderLineId\"")}
        """;

    /// <summary>仅源履约临时表字段的局部JSON关系；墙上时间无时区，所有未知量保留NULL。</summary>
    private const string FulfillmentRelation = """
        SELECT f."SalesOrderLineId" AS sales_order_line_id,f."GrossDemandId" AS gross_demand_id
        FROM jsonb_to_recordset(CAST(@FulfillmentLines AS jsonb)) AS f(
            "SalesOrderLineId" uuid,"LineNumber" integer,"Sku" text,"CustomerSku" text,"OrderedQty" numeric,
            "UnitSnapshot" text,"EffectiveDueDate" timestamp without time zone,"GrossDemandId" uuid,
            "GrossDemandQty" numeric,"GrossDemandUnitSnapshot" text,"GrossDemandVersion" integer,"SourceState" text,
            "ShippedQty" numeric,"UnshippedQty" numeric,"AllowedFurtherDeliveryQty" numeric,"ReviewReason" text)
        """;

    /// <summary>读取当前产品单位；仅根Net允许回落到Gross单位，绝不回落Net单位快照。</summary>
    private static string FulfillmentUnit => "COALESCE(NULLIF(btrim(p.unit,' '),''),CASE WHEN net.parent_net_demand_id IS NULL THEN NULLIF(btrim(gross.unit_snapshot,' '),'') END)";

    /// <summary>采购单位异常沿源NULL三值逻辑与ASCII修剪判定，不将Unicode空白扩展为缺值。</summary>
    private static string FulfillmentBadUnit => $"NULLIF(btrim(pol.unit_snapshot,' '),'') IS NULL OR {FulfillmentUnit} IS NULL OR {Different("btrim(pol.unit_snapshot,' ')", FulfillmentUnit)}";

    /// <summary>采购行粒度的净关联量及整行数量/最新反馈，不分摊货好量；客户和单位异常保持源原因优先级。</summary>
    private static string CreateFulfillmentPurchasesSql() => $"""
        WITH fl AS ({FulfillmentRelation})
        SELECT fl.sales_order_line_id AS "SalesOrderLineId",po.uid AS "PurchaseOrderId",pol.uid AS "PurchaseOrderLineId",
               pol.line_number AS "PurchaseLineNumber",po.order_no AS "PurchaseOrderNo",pol.sku AS "Sku",
               COALESCE(pol.unit_snapshot,'') AS "UnitSnapshot",po.confirmation_status AS "ConfirmationStatus",po.lifecycle_status AS "LifecycleStatus",
               CASE WHEN po.customer_id IS NULL OR po.customer_id<>gross.customer_id
                    OR MAX(CASE WHEN {FulfillmentBadUnit} THEN 1 ELSE 0 END)>0
                    THEN NULL ELSE SUM(link.allocated_qty-link.released_qty) END AS "LinkedQty",
               pol.quantity AS "PurchaseLineQty",
               CASE WHEN po.customer_id IS NULL OR po.customer_id<>gross.customer_id THEN '采购客户归属需核实'
                    WHEN MAX(CASE WHEN {FulfillmentBadUnit} THEN 1 ELSE 0 END)>0 THEN '采购行与物料单位需核实'
                    ELSE '' END AS "ReviewReason",
               EXISTS (
                   SELECT 1 FROM {FulfillmentLinks} other_link
                   JOIN {FulfillmentNets} other_net ON other_net.uid=other_link.net_demand_id AND other_net.is_deleted=FALSE AND other_net.lifecycle_status=@NetActive
                   JOIN {Gross} other_gross ON other_gross.uid=other_net.gross_demand_id AND other_gross.is_deleted=FALSE AND other_gross.lifecycle_status=@DemandActive
                   WHERE NOT ({Different("other_link.target_type", "'PO'")}) AND other_link.target_line_id=pol.uid
                     AND other_link.is_deleted=FALSE AND other_link.link_status=@LinkActive
                     AND other_link.allocated_qty>other_link.released_qty AND other_net.gross_demand_id<>fl.gross_demand_id
               ) AS "IsSharedPurchaseLine",latest.uid IS NOT NULL AS "HasProgress",
               latest.ready_qty AS "PurchaseLineReadyQty",latest.expected_ready_date AS "ExpectedReadyDate",latest.supplier_reported_date AS "SupplierReportedDate"
        FROM fl JOIN {Gross} gross ON gross.uid=fl.gross_demand_id AND gross.lifecycle_status=@DemandActive
        JOIN {FulfillmentNets} net ON net.gross_demand_id=gross.uid AND net.is_deleted=FALSE AND net.lifecycle_status=@NetActive
            AND net.source_demand_version_snapshot=gross.version AND net.source_version_snapshot=gross.source_version
            AND NOT ({Different("net.source_demand_basis_fingerprint_snapshot", "gross.demand_basis_fingerprint")})
        LEFT JOIN {FulfillmentProducts} p ON p.uid=net.product_id AND p.is_deleted=FALSE
        JOIN {FulfillmentLinks} link ON link.net_demand_id=net.uid AND link.is_deleted=FALSE AND link.link_status=@LinkActive
            AND NOT ({Different("link.target_type", "'PO'")}) AND link.allocated_qty>link.released_qty
        JOIN {FulfillmentPurchaseLines} pol ON pol.uid=link.target_line_id AND pol.is_deleted=FALSE AND pol.purchase_order_id=link.target_head_id
        JOIN {FulfillmentPurchaseHeads} po ON po.uid=link.target_head_id AND po.is_deleted=FALSE AND po.lifecycle_status<>@PurchaseVoided
        LEFT JOIN LATERAL (
            SELECT report.uid,report.ready_qty,report.expected_ready_date,report.supplier_reported_date
            FROM {FulfillmentReports} report WHERE report.purchase_order_line_id=pol.uid AND report.is_deleted=FALSE
            ORDER BY report.sequence DESC,{SourceUuidOrder("report.uid")} DESC LIMIT 1
        ) latest ON TRUE
        GROUP BY fl.sales_order_line_id,fl.gross_demand_id,gross.customer_id,gross.unit_snapshot,
                 po.uid,pol.uid,pol.line_number,po.order_no,po.customer_id,pol.sku,pol.unit_snapshot,
                 po.confirmation_status,po.lifecycle_status,pol.quantity,latest.uid,latest.ready_qty,latest.expected_ready_date,latest.supplier_reported_date
        ORDER BY po.order_no COLLATE public.erp_dictionary_ci NULLS FIRST,pol.line_number,{SourceUuidOrder("pol.uid")}
        """;

    /// <summary>Posted与Reversed均展示历史正量，只有Posted计净交付；日期和身份沿源NULL方向/SqlGuid尾键。</summary>
    private static string CreateFulfillmentDeliveriesSql() => $"""
        WITH fl AS ({FulfillmentRelation})
        SELECT fl.sales_order_line_id AS "SalesOrderLineId",ph.uid AS "PackingListId",pl.uid AS "PackingListLineId",
               ph.packing_list_no AS "PackingListNo",pl.line_number AS "LineNumber",pl.qty AS "Qty",
               COALESCE(pl.unit_snapshot,'') AS "UnitSnapshot",ph.posting_status=@Posted AS "CountsTowardDelivery",
               ph.posted_date AS "PostedDate",ph.reversed_date AS "ReversedDate"
        FROM fl JOIN {PackingLines} pl ON pl.gross_demand_id=fl.gross_demand_id AND pl.is_deleted=FALSE
        JOIN {PackingHeads} ph ON ph.uid=pl.packing_list_head_id AND ph.is_deleted=FALSE
        WHERE ph.posting_status IN (@Posted,@Reversed)
        ORDER BY ph.posted_date NULLS FIRST,{SourceUuidOrder("ph.uid")},pl.line_number,{SourceUuidOrder("pl.uid")}
        """;

    /// <summary>活动正净分配的目标或版本异常独立标记，不能因采购查询过滤掉坏证据而误报未关联。</summary>
    private static string CreateFulfillmentInvalidLinksSql() => $"""
        WITH fl AS ({FulfillmentRelation})
        SELECT DISTINCT fl.sales_order_line_id AS "SalesOrderLineId"
        FROM fl JOIN {Gross} gross ON gross.uid=fl.gross_demand_id AND gross.lifecycle_status=@DemandActive
        JOIN {FulfillmentNets} net ON net.gross_demand_id=gross.uid AND net.is_deleted=FALSE
        LEFT JOIN {FulfillmentProducts} p ON p.uid=net.product_id AND p.is_deleted=FALSE
        JOIN {FulfillmentLinks} link ON link.net_demand_id=net.uid AND link.is_deleted=FALSE AND link.link_status=@LinkActive
            AND NOT ({Different("link.target_type", "'PO'")}) AND link.allocated_qty>link.released_qty
        LEFT JOIN {FulfillmentPurchaseLines} pol ON pol.uid=link.target_line_id AND pol.is_deleted=FALSE AND pol.purchase_order_id=link.target_head_id
        LEFT JOIN {FulfillmentPurchaseHeads} po ON po.uid=link.target_head_id AND po.is_deleted=FALSE
        WHERE net.lifecycle_status<>@NetActive OR net.source_demand_version_snapshot IS NULL OR net.source_demand_version_snapshot<>gross.version
           OR net.source_version_snapshot IS NULL OR net.source_version_snapshot<>gross.source_version
           OR net.source_demand_basis_fingerprint_snapshot IS NULL OR {Different("net.source_demand_basis_fingerprint_snapshot", "gross.demand_basis_fingerprint")}
           OR pol.uid IS NULL OR po.uid IS NULL OR po.lifecycle_status=@PurchaseVoided OR {FulfillmentBadUnit}
        """;
}
