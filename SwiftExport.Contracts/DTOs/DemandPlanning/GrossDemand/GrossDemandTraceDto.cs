using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    public class GrossDemandTraceDto
    {
        public GrossDemandDetailDto GrossDemand { get; set; }
        public List<NetDemandTraceItemDto> NetDemands { get; set; } = new List<NetDemandTraceItemDto>();
        public List<DemandExecutionTraceItemDto> ExecutionLinks { get; set; } = new List<DemandExecutionTraceItemDto>();
    }
}
