using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds
{
    /// <summary>草稿命令成功结果，可由同一命令重放；客户端按 Uid 重新读取详情。</summary>
    public class InboundDraftCommandResult
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string DocumentNo { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
    }
}

