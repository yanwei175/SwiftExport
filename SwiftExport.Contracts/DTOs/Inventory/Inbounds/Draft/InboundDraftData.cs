using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft
{
    /// <summary>人工草稿可编辑内容；不接收客户端库存状态、供应商或产品快照。</summary>
    public class InboundDraftData
    {
        public InboundDocumentTypeEnum DocumentType { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public DateTime ReceiptDate { get; set; }
        public string Remark { get; set; } = string.Empty;
        public List<InboundDraftLineInput> Lines { get; set; } = new List<InboundDraftLineInput>();
    }
}

