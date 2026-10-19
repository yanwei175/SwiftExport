namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>以采购单稳定身份打开只读追溯文档。</summary>
public sealed partial class PurchaseOrderTraceOpenRequest
{
    /// <summary>需要追溯的采购单 Uid。</summary>
    public Guid PurchaseOrderId { get; init; }
    /// <summary>可选的采购行稳定身份，仅定位已打开追溯中的行，不改变页签身份。</summary>
    public Guid? PurchaseOrderLineId { get; init; }
}
