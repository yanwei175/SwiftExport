using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>单次过账或整单反向的实际执行证据。</summary>
    public class PackingListExecutionTraceDto : BaseDto
    {
        public Guid PackingListHeadId { get; set; }
        public Guid CommandId { get; set; }
        public int Operation { get; set; }
        public Guid? OriginalExecutionId { get; set; }
        public DateTime BusinessDate { get; set; }
        public string PackingListNo { get; set; }
        public int RuleVersion { get; set; }
        public string Reason { get; set; }
        public List<PackingListExecutionLineTraceDto> Lines { get; set; } = new List<PackingListExecutionLineTraceDto>();
    }
}
