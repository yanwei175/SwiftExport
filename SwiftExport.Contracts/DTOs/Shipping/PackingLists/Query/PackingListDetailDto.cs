using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>装箱单专用数据契约；后端待实现。</summary>
    public class PackingListDetailDto
    {
        /// <summary>完整头快照。</summary>
        public PackingListHeaderDto Header { get; set; }
        /// <summary>有效物理来源行，保持行号顺序。</summary>
        public List<PackingListLineDto> Lines { get; set; } = new List<PackingListLineDto>();
    }
}
