using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>出库列表摘要，不包含整单编辑明细。</summary>
    public class OutboundManagerListItemDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string DocumentNo { get; set; } = string.Empty;
        public OutboundDocumentTypeEnum DocumentType { get; set; }
        public InventoryDocumentLifecycleStatusEnum LifecycleStatus { get; set; }
        public OutboundPostingStatusEnum PostingStatus { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public string WarehouseNameSnapshot { get; set; } = string.Empty;
        public DateTime ShipmentDate { get; set; }
        public string Remark { get; set; } = string.Empty;
        /// <summary>手工出库用途，最多 500 字；草稿可空，过账时必填。</summary>
        public string IssueReason { get; set; } = string.Empty;
        public int LineCount { get; set; }
        public decimal TotalQuantity { get; set; }
    }
}
