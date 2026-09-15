using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>手工出库整单反过账只读预览与阻断信息。</summary>
    public class OutboundReversePreviewDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string DocumentNo { get; set; }
        public string WarehouseName { get; set; }
        public string IssueReason { get; set; }
        public bool CanReverse { get; set; }
        public List<OutboundReverseImpactDto> StockImpacts { get; set; } = new List<OutboundReverseImpactDto>();
        public List<string> Blockers { get; set; } = new List<string>();
    }
}

