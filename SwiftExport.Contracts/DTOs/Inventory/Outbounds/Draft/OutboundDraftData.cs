using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>人工草稿可编辑内容；不接收客户端库存状态、供应商或产品快照。</summary>
    public class OutboundDraftData
    {
        public OutboundDocumentTypeEnum DocumentType { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public DateTime ShipmentDate { get; set; }
        public string Remark { get; set; } = string.Empty;
        /// <summary>手工出库用途，最多 500 字；草稿可空，过账时必填。</summary>
        public string IssueReason { get; set; } = string.Empty;
        public List<OutboundDraftLineInput> Lines { get; set; } = new List<OutboundDraftLineInput>();
    }
}
