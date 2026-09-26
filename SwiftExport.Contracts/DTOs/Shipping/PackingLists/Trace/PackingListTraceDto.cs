using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>箱单完整执行追踪；缺少执行记录不代表允许猜测历史库存。</summary>
    public class PackingListTraceDto
    {
        public Guid PackingListId { get; set; }
        public string PackingListNo { get; set; }
        public bool HasExecutionEvidence { get; set; }
        public string Notice { get; set; }
        public List<PackingListExecutionTraceDto> Executions { get; set; } = new List<PackingListExecutionTraceDto>();
    }
}
