using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds
{
    /// <summary>入库列表摘要，不包含整单编辑明细。</summary>
    public class InboundManagerListItemDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string DocumentNo { get; set; } = string.Empty;
        public InboundDocumentTypeEnum DocumentType { get; set; }
        public InboundCreationModeEnum CreationMode { get; set; }
        public InventoryDocumentLifecycleStatusEnum LifecycleStatus { get; set; }
        public InboundPostingStatusEnum PostingStatus { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public string WarehouseNameSnapshot { get; set; } = string.Empty;
        public DateTime ReceiptDate { get; set; }
        public string Remark { get; set; } = string.Empty;
        public int LineCount { get; set; }
        public decimal TotalQuantity { get; set; }
    }
}

