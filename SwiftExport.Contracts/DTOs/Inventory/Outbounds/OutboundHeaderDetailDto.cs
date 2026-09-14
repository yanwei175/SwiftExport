using System;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>出库单头详情数据；不包含勾选与编辑状态，不作为保存命令。</summary>
    public class OutboundHeaderDetailDto : BaseDto
    {
        public string DocumentNo { get; set; } = string.Empty;
        public OutboundDocumentTypeEnum DocumentType { get; set; } = OutboundDocumentTypeEnum.StockIssue;
        public InventoryDocumentLifecycleStatusEnum LifecycleStatus { get; set; } = InventoryDocumentLifecycleStatusEnum.Active;
        public OutboundPostingStatusEnum PostingStatus { get; set; } = OutboundPostingStatusEnum.Unposted;
        public Guid WarehouseSiteId { get; set; }
        public string WarehouseCodeSnapshot { get; set; } = string.Empty;
        public string WarehouseNameSnapshot { get; set; } = string.Empty;
        public DateTime ShipmentDate { get; set; } = DateTime.Today;
        public string Remark { get; set; } = string.Empty;
        public DateTime? PostedDate { get; set; }
        public string PostedBy { get; set; } = string.Empty;
        public DateTime? ReversedDate { get; set; }
        public string ReversedBy { get; set; } = string.Empty;
        public string ReverseReason { get; set; } = string.Empty;
        public Guid? TriggerPackingListId { get; set; }
        public Guid? CommandId { get; set; }
    }
}
