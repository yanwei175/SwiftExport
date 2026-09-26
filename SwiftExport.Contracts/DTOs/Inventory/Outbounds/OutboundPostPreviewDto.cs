using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>过账预览快照，不构成库存预留。</summary>
    public class OutboundPostPreviewDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string DocumentNo { get; set; }
        /// <summary>本次待过账单据保存的出库原因，不接受预览端改写。</summary>
        public string IssueReason { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public string WarehouseName { get; set; }
        public bool CanPost { get; set; }
        public List<OutboundPostStockImpactDto> StockImpacts { get; set; } = new List<OutboundPostStockImpactDto>();
        public List<OutboundPostBlockerDto> Blockers { get; set; } = new List<OutboundPostBlockerDto>();
    }
}
