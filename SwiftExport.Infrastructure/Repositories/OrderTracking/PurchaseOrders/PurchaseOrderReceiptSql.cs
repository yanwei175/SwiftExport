using SwiftExport.Infrastructure.Repositories.OrderTracking.Receipts;

namespace SwiftExport.Infrastructure.Repositories.OrderTracking.PurchaseOrders;

/// <summary>采购跟踪只确定可见采购行范围，正式收货可信度由共同事实表达负责。</summary>
internal static class PurchaseOrderReceiptSql
{
    /// <summary>仅接收源码范围谓词；无收货权限时范围为空，不读取其他维度的证据。</summary>
    internal static string Ctes(string headScope) => PurchaseReceiptFactsSql.Ctes($"""
        SELECT l.uid FROM public.purchase_order_line l JOIN public.purchase_order_head h ON h.uid=l.purchase_order_id
        WHERE NOT l.is_deleted AND NOT h.is_deleted AND ({headScope})
        """);
}
