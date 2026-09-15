using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>手工出库整单反过账已提交结果快照。</summary>
    public class OutboundReverseResult
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string DocumentNo { get; set; }
        public OutboundPostingStatusEnum PostingStatus { get; set; }
        public DateTime ReversedDate { get; set; }
        public string ReversedBy { get; set; }
        public string ReverseReason { get; set; }
    }
}

