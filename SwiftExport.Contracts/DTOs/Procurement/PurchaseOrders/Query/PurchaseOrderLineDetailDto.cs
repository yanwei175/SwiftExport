using System;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto
{
    /// <summary>采购详情行的身份、商务快照和来源摘要；不包含跟进、入库、库存或 Trace 明细。</summary>
    public sealed class PurchaseOrderLineDetailDto : BaseDto
    {
        public int LineNumber { get; set; }
        public Guid ProductId { get; set; }
        /// <summary>是否没有产品主档身份；非标行的旧 ProductId 投影为 Guid.Empty，不能据 SKU 前缀判断。</summary>
        public bool IsNonStandard { get; set; }
        public string MainImage { get; set; }
        public string Sku { get; set; }
        public string CustomerSku { get; set; }
        public string LinkSku { get; set; }
        public string SupplierSku { get; set; }
        public string DescriptionZh { get; set; }
        public string FinishZh { get; set; }
        public string FeaturesZh { get; set; }
        public string MaterialType { get; set; }
        public string SupplyType { get; set; }
        public string UnitSnapshot { get; set; }
        public decimal Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }
        public decimal? UnitAmount { get; set; }
        public decimal? TaxAmount { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public Guid DeliverySupplierId { get; set; }
        public string DeliverySupplierCode { get; set; }
        public string DeliverySupplierName { get; set; }
        public string SourceType { get; set; }
        public string SourceNo { get; set; }
        public Guid? SourceUid { get; set; }
        public Guid? SourceLineId { get; set; }
        public int? SourceLineNumber { get; set; }
        public string SubNote { get; set; }
    }
}
