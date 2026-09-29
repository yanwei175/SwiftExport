using System;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace
{
    /// <summary>以采购行为来源的正式入库行；反向行保留原数量但净贡献为零。</summary>
    public sealed class PurchaseOrderTraceReceiptDto
    {
        public Guid InboundDocumentId { get; set; }
        public Guid InboundLineId { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public string DocumentNo { get; set; }
        public InboundPostingStatusEnum PostingStatus { get; set; }
        public decimal Quantity { get; set; }
        public decimal NetQuantity { get; set; }
        public string UnitSnapshot { get; set; }
        public string WarehouseNameSnapshot { get; set; }
        public DateTime ReceiptDate { get; set; }
        public DateTime? PostedDate { get; set; }
        public string PostedBy { get; set; }
        public DateTime? ReversedDate { get; set; }
        public string ReversedBy { get; set; }
        public string ReverseReason { get; set; }
        public Guid? TriggerPackingListId { get; set; }
    }
}
