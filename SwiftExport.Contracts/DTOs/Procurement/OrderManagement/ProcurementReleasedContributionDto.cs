using System;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>本次拟解除的需求贡献；保留来源身份，不表示物料需求已回退。</summary>
    public sealed class ProcurementReleasedContributionDto
    {
        public Guid LinkId { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public Guid NetDemandId { get; set; }
        public Guid GrossDemandId { get; set; }
        public string SourceNo { get; set; }
        public string Sku { get; set; }
        public decimal Quantity { get; set; }
    }
}
