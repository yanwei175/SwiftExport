using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace
{
    /// <summary>采购行及其历史证据；已删除行仍可追溯，不参与当前管理状态。</summary>
    public sealed class PurchaseOrderTraceLineDto
    {
        public Guid PurchaseOrderLineId { get; set; }
        public int LineNumber { get; set; }
        public bool IsDeleted { get; set; }
        public string Sku { get; set; }
        public string UnitSnapshot { get; set; }
        public decimal Quantity { get; set; }
        public decimal? LatestReadyQty { get; set; }
        public decimal? FormalReceivedQty { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public decimal? UnlinkedQty { get; set; }
        public string ReviewReason { get; set; }
        public List<PurchaseOrderTraceLinkDto> Links { get; set; } = new List<PurchaseOrderTraceLinkDto>();
        public List<PurchaseOrderTraceProgressDto> ProgressHistory { get; set; } = new List<PurchaseOrderTraceProgressDto>();
        public List<PurchaseOrderTraceReceiptDto> Receipts { get; set; } = new List<PurchaseOrderTraceReceiptDto>();
    }
}
