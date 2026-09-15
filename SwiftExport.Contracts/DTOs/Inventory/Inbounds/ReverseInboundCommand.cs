using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds
{
    /// <summary>手工入库整单反过账命令；不接受客户端指定库存身份或数量。</summary>
    public class ReverseInboundCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
