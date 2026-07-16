using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    public class DemandExecutionTraceItemDto
    {
        public Guid Uid { get; set; }
        public Guid NetDemandId { get; set; }
        public DemandExecutionLinkStatusEnum LinkStatus { get; set; }
        public string TargetType { get; set; }
        public Guid TargetHeadId { get; set; }
        public string TargetNo { get; set; }
        public Guid TargetLineId { get; set; }
        public decimal AllocatedQty { get; set; }
        public decimal CompletedQty { get; set; }
        public decimal ReceivedQty { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string CancelledBy { get; set; }
        public string CancelledReason { get; set; }
        public DateTime? ReversedAt { get; set; }
        public string ReversedBy { get; set; }
        public string ReversedReason { get; set; }
        public string Remark { get; set; }
    }
}
