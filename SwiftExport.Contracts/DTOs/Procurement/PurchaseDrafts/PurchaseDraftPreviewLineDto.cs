using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购草稿预览行及其来源贡献。</summary>
    public class PurchaseDraftPreviewLineDto
    {
        public Guid? ProductId { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public string Color { get; set; }
        public string Specification { get; set; }
        public string ImagePath { get; set; }
        public string MaterialType { get; set; }
        public bool IsCustomerSupplied { get; set; }
        public string Unit { get; set; }
        public int QuantityScale { get; set; }
        public string SupplierSku { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }
        public decimal PurchaseQty { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public Guid? DeliveryTargetSupplierId { get; set; }
        public string DeliveryTargetSupplierCode { get; set; }
        public string DeliveryTargetSupplierName { get; set; }
        public bool PurchaseDataIncomplete { get; set; }
        /// <summary>同 SKU 未合并时的可理解差异说明；可兼容或保持来源明细时为空。</summary>
        public string MergeDifferenceReason { get; set; }
        public List<DateTime?> SourcePlannedDeliveryDates { get; set; } = new List<DateTime?>();
        public List<PurchaseDraftSourceContributionDto> Contributions { get; set; } = new List<PurchaseDraftSourceContributionDto>();
    }
}
