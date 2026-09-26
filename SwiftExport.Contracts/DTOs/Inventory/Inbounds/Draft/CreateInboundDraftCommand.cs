using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft
{
    /// <summary>建立人工入库草稿，同一业务重试复用 CommandId。</summary>
    public class CreateInboundDraftCommand
    {
        public Guid CommandId { get; set; }
        public InboundDraftData Draft { get; set; } = new InboundDraftData();
    }
}

