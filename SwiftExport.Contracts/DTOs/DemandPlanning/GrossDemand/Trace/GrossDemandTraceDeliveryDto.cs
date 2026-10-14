using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand.Trace
{
    /// <summary>成品需求的装箱正式交付或反向历史行。</summary>
    public class GrossDemandTraceDeliveryDto
    {
        public Guid PackingListId { get; set; }
        public Guid PackingListLineId { get; set; }
        public string PackingListNo { get; set; }
        public int LineNumber { get; set; }
        public decimal Qty { get; set; }
        public string Unit { get; set; }
        public bool CountsTowardDelivery { get; set; }
        public DateTime? PostedDate { get; set; }
        public DateTime? ReversedDate { get; set; }
        public string ReviewReason { get; set; }
    }
}
