// 第5阶段性能对照：本轮开始前的源码快照，仅重命名类型/命名空间及等价逾期请求适配，不作生产接线。
namespace SwiftExport.Application.Tests.OrderTracking.Baseline;

/// <summary>总览与基础明细共用同口径事实投影；仅客户显示和销售来源补充字段来自关联表。</summary>
internal static class BaselineOrderTrackingFactsSql
{
    // LEFT JOIN 保留客户或来源头缺失的需求，SO 类型约束防止其他来源的相同 UUID 误取订单信息。
    internal const string From = """
        FROM public.gross_demand g
        LEFT JOIN public.customer c ON c.uid=g.customer_id AND c.is_deleted=FALSE
        LEFT JOIN public.sales_order_header so ON so.uid=g.source_uid AND so.is_deleted=FALSE
            AND rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='SO'
        """;
    internal const string Eligible = "g.is_deleted=FALSE AND rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci IN ('SO','CR','MD')";
    internal const string Columns = """
        g.uid AS "GrossDemandId",g.version AS "Version",g.source_type AS "SourceType",
        g.source_uid AS "SourceUid",g.source_line_id AS "SourceLineId",g.source_no AS "SourceNo",
        g.source_line_number AS "SourceLineNumber",g.customer_id AS "CustomerId",
        c.customer_code AS "CustomerCode",c.customer_name AS "CustomerName",
        so.customer_po_no AS "CustomerPo",so.invoice_no AS "InvoiceNo",
        g.product_id AS "ProductId",g.is_non_standard AS "IsNonStandard",g.sku_snapshot AS "Sku",
        g.customer_sku_snapshot AS "CustomerSku",g.link_sku_snapshot AS "LinkSku",
        g.image_path_snapshot AS "ImagePath",g.description_snapshot AS "Description",
        g.color_snapshot AS "Color",g.specification_snapshot AS "Specification",
        g.demand_qty AS "DemandQty",g.unit_snapshot AS "Unit",
        g.required_date AS "RequiredDate",g.lifecycle_status AS "LifecycleStatus"
        """;
}
