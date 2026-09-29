using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment
{
    /// <summary>一条销售行的来源、正式交付和商业未交事实。</summary>
    public sealed class SalesFulfillmentLineDto
    {
        public Guid SalesOrderLineId { get; set; }
        public int LineNumber { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string CustomerSku { get; set; } = string.Empty;
        public decimal OrderedQty { get; set; }
        public string UnitSnapshot { get; set; } = string.Empty;
        public DateTime? EffectiveDueDate { get; set; }
        public Guid? GrossDemandId { get; set; }
        public decimal? GrossDemandQty { get; set; }
        public string GrossDemandUnitSnapshot { get; set; } = string.Empty;
        public int? GrossDemandVersion { get; set; }
        public string SourceState { get; set; } = string.Empty;
        public string PurchaseState { get; set; } = string.Empty;
        public string PurchaseReviewReason { get; set; } = string.Empty;
        public decimal? ShippedQty { get; set; }
        public decimal? UnshippedQty { get; set; }
        public decimal? AllowedFurtherDeliveryQty { get; set; }
        public string ReviewReason { get; set; } = string.Empty;
        public List<SalesFulfillmentPurchaseDto> Purchases { get; set; } = new List<SalesFulfillmentPurchaseDto>();
        public List<SalesFulfillmentDeliveryDto> Deliveries { get; set; } = new List<SalesFulfillmentDeliveryDto>();
    }
}
