namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>采购订单管理页的新链从表只读投影；不承载编辑、来源贡献集合或进度历史。</summary>
public sealed partial class PurchaseOrderManagementLineModel
{
    public int LineNumber { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string MainImage { get; set; } = string.Empty;
    public string CustomerSku { get; set; } = string.Empty;
    public string LinkSku { get; set; } = string.Empty;
    public string SupplierSku { get; set; } = string.Empty;
    public string DescriptionZh { get; set; } = string.Empty;
    public string FinishZh { get; set; } = string.Empty;
    public string FeaturesZh { get; set; } = string.Empty;
    public string MaterialType { get; set; } = string.Empty;
    public string SupplyType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string UnitSnapshot { get; set; } = string.Empty;
    public string DeliverySupplierName { get; set; } = string.Empty;
    public DateTime? PlannedDeliveryDate { get; set; }
    public decimal? TaxPrice { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? UnitAmount { get; set; }
    public decimal? UnreceivedQty { get; set; }
    public string LatestReadyQtyText { get; set; } = string.Empty;
    public string ExpectedCompletionDateText { get; set; } = string.Empty;
    public string LatestFollowUpDateText { get; set; } = string.Empty;
    public string LatestFollowUpUserText { get; set; } = string.Empty;
    public string SourceSummary { get; set; } = string.Empty;
    public string SubNote { get; set; } = string.Empty;
}
