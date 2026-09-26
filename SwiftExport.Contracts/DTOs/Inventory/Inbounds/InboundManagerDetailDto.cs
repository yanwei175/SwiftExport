using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Inventory.Inbounds
{
    /// <summary>单张入库单的完整编辑读取结果，头行版本以单头为整单并发边界。</summary>
    public class InboundManagerDetailDto
    {
        public InboundHeaderDetailDto Header { get; set; } = new InboundHeaderDetailDto();
        public List<InboundLineDetailDto> Lines { get; set; } = new List<InboundLineDetailDto>();
    }
}

