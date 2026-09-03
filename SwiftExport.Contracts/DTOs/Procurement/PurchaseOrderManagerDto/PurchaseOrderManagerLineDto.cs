using System;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto
{
    /// <summary>采购管理页的新链从表摘要；图片是 Product.MainImage 当前投影，不是采购历史快照。</summary>
    public sealed class PurchaseOrderManagerLineDto
    {
        public Guid PurchaseOrderLineId { get; set; }
        public int Version { get; set; }
        public Guid ProductId { get; set; }
        public int LineNumber { get; set; }
        public string MainImage { get; set; }
        public string CustomerSku { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string SupplierSku { get; set; }
        public string DescriptionZh { get; set; }
        public string FinishZh { get; set; }
        public string FeaturesZh { get; set; }
        public string MaterialType { get; set; }
        public string SupplyType { get; set; }
        public decimal Quantity { get; set; }
        public decimal ReceivedQty { get; set; }
        public decimal UnreceivedQty { get; set; }
        public decimal ReadyQty { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public DateTime? ExpectedCompletionDate { get; set; }
        public DateTime? LatestFollowUpDate { get; set; }
        public string FollowUpUserName { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }
        public decimal? UnitAmount { get; set; }
        public decimal? TaxAmount { get; set; }
        public string DeliverySupplierName { get; set; }
        public string SourceType { get; set; }
        public string SourceNo { get; set; }
        public int? SourceLineNumber { get; set; }
        public string SubNote { get; set; }
    }
}
