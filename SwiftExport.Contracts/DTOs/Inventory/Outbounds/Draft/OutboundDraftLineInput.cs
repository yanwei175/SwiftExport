using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>草稿行输入；已有行携带出库行身份，新行 Uid 为空。来源版本不作为保存准入。</summary>
    public class OutboundDraftLineInput
    {
        public Guid Uid { get; set; }
        public Guid ProductId { get; set; }
        public decimal Quantity { get; set; }
    }
}
