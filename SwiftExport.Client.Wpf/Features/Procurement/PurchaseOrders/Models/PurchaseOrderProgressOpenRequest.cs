namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>以采购单及可选采购行定位反馈文档，不传递页面或可变 DTO。</summary>
public sealed class PurchaseOrderProgressOpenRequest
{
    /// <summary>反馈所属采购单的稳定身份。</summary>
    public Guid PurchaseOrderId { get; init; }
    /// <summary>首次打开或干净文档再次激活时希望定位的采购行。</summary>
    public Guid? PurchaseOrderLineId { get; init; }
}
