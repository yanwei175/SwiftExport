namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>以采购单稳定身份打开只读追溯文档。</summary>
public sealed class PurchaseOrderTraceOpenRequest
{
    /// <summary>需要追溯的采购单 Uid。</summary>
    public Guid PurchaseOrderId { get; init; }
}
