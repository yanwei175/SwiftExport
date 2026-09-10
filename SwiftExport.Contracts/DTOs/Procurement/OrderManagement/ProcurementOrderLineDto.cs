using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>订单行固定身份、单位和收货目标快照及商务内容，不返回跟进或来源集合。</summary>
    public sealed class ProcurementOrderLineDto
    {
        public Guid PurchaseOrderLineId { get; set; }
        public int LineNumber { get; set; }
        public int Version { get; set; }
        public Guid ProductId { get; set; }
        public string Sku { get; set; }
        public string CustomerSku { get; set; }
        public string LinkSku { get; set; }
        public string FinishZh { get; set; }
        public string FeaturesZh { get; set; }
        public string MaterialType { get; set; }
        public string SupplyType { get; set; }
        public string UnitSnapshot { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnlinkedQty { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public string WarehouseCode { get; set; }
        public string WarehouseName { get; set; }
        public Guid DeliverySupplierId { get; set; }
        public string DeliverySupplierCode { get; set; }
        public string DeliverySupplierName { get; set; }
        public string SourceType { get; set; }
        public string SourceNo { get; set; }
        public Guid? SourceUid { get; set; }
        public Guid? SourceLineId { get; set; }
        public int? SourceLineNumber { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }
        public decimal? UnitAmount { get; set; }
        public decimal? TaxAmount { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public string SupplierSku { get; set; }
        public string DescriptionZh { get; set; }
        public string SubNote { get; set; }
        public ProcurementOrderProgressSummaryDto LatestProgress { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string UpdatedBy { get; set; }
    }
}
