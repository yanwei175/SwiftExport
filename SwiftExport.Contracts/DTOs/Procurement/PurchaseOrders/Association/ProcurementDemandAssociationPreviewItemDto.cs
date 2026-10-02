using System;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>预览单条贡献与采购、需求本次汇总分配后的剩余量。</summary>
    public sealed class ProcurementDemandAssociationPreviewItemDto
    {
        public Guid CommandItemId { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public int LineNumber { get; set; }
        public string PurchaseSku { get; set; }
        public Guid NetDemandId { get; set; }
        public string SourceNo { get; set; }
        public string DemandSku { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal LineUnlinkedBefore { get; set; }
        public decimal LineUnlinkedAfter { get; set; }
        public decimal DemandRemainingBefore { get; set; }
        public decimal DemandRemainingAfter { get; set; }
    }
}
