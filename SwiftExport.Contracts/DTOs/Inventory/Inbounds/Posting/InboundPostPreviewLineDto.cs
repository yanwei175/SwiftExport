using System;
namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting
{
    /// <summary>本次采购入库与实时来源容量对照；草稿提示不扣额度。</summary>
    public class InboundPostPreviewLineDto
    {
        public Guid Uid { get; set; }
        public Guid SourceLineId { get; set; }
        public Guid ProductId { get; set; }
        public int LineNumber { get; set; }
        public string SourceNo { get; set; }
        public string Sku { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal OriginalQty { get; set; }
        public decimal PostedNetQty { get; set; }
        public decimal RemainingQty { get; set; }
        public int OtherDraftCount { get; set; }
        public decimal OtherDraftQuantity { get; set; }
    }
}
