using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>建立人工出库草稿，同一业务重试复用 CommandId。</summary>
    public class CreateOutboundDraftCommand
    {
        public Guid CommandId { get; set; }
        public OutboundDraftData Draft { get; set; } = new OutboundDraftData();
    }
}
