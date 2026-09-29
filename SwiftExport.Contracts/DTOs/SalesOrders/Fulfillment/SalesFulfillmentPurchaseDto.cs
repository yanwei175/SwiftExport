using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment
{
    /// <summary>有效需求 Link 对应的采购行贡献；货好数量始终属于整个采购行。</summary>
    public class SalesFulfillmentPurchaseDto
    {
        /// <summary>用于把采购证据归入销售行，不表示采购行只有一个来源。</summary>
        public Guid SalesOrderLineId { get; set; }
        public Guid PurchaseOrderId { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public int PurchaseLineNumber { get; set; }
        public string PurchaseOrderNo { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string UnitSnapshot { get; set; } = string.Empty;
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        /// <summary>有效 Link 贡献；采购客户或单位无法核实时为空。</summary>
        public decimal? LinkedQty { get; set; }
        public decimal PurchaseLineQty { get; set; }
        public string ReviewReason { get; set; } = string.Empty;
        public bool IsSharedPurchaseLine { get; set; }
        public bool HasProgress { get; set; }
        public decimal? PurchaseLineReadyQty { get; set; }
        public DateTime? ExpectedReadyDate { get; set; }
        public DateTime? SupplierReportedDate { get; set; }
    }
}
