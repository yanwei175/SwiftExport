using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds
{
    /// <summary>软删除未过账人工草稿，保留审计记录，不形成反向流水。</summary>
    public class DeleteInboundDraftCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}

