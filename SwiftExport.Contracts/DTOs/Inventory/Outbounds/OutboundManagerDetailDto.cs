using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Outbounds
{
    /// <summary>单张出库单的完整编辑读取结果，头行版本以单头为整单并发边界。</summary>
    public class OutboundManagerDetailDto
    {
        public OutboundHeaderDetailDto Header { get; set; } = new OutboundHeaderDetailDto();
        public List<OutboundLineDetailDto> Lines { get; set; } = new List<OutboundLineDetailDto>();
    }
}
