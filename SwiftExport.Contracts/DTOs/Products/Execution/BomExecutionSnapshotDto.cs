using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Products.Execution
{
    /// <summary>完整一致读取的当前 BOM 执行依据；不是可编辑 BOM，也不包含仓库选择。</summary>
    public class BomExecutionSnapshotDto
    {
        public Guid RootProductId { get; set; }
        public string Fingerprint { get; set; }
        public bool HasBom { get; set; }
        public List<BomExecutionNodeDto> Nodes { get; set; } = new List<BomExecutionNodeDto>();
    }
}
