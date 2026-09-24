using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft
{
    /// <summary>草稿行输入；已有行携带入库行身份，新行 Uid 为空。来源版本不作为保存准入。</summary>
    public class InboundDraftLineInput
    {
        public Guid Uid { get; set; }
        public Guid SourceLineId { get; set; }
        public decimal Quantity { get; set; }
    }
}

