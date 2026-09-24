using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft
{
    /// <summary>以完整明细集合更新人工草稿；省略已有行表示删除，整单原子提交。</summary>
    public class UpdateInboundDraftCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public InboundDraftData Draft { get; set; } = new InboundDraftData();
    }
}

