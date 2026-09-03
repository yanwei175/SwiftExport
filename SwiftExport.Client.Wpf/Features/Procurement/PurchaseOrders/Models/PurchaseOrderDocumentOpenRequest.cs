namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>采购详情窄打开请求；显式区分新链和只读历史链，避免详情按字段猜测链路。</summary>
public sealed class PurchaseOrderDocumentOpenRequest
{
    public Guid PurchaseOrderId { get; set; }
    public bool IsLegacy { get; set; }
}
