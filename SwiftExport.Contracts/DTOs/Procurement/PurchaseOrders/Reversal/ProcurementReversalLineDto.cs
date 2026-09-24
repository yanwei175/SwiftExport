using System;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>本次反向操作涉及的原采购行，仅用于身份及影响展示。</summary>
    public sealed class ProcurementReversalLineDto
    {
        public Guid PurchaseOrderLineId { get; set; }
        public int Version { get; set; }
        public int LineNumber { get; set; }
        public string Sku { get; set; }
        public decimal Quantity { get; set; }
    }
}
