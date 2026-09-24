using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    public class GrossDemandSourceGroupDto
    {
        public Guid SourceUid { get; set; }
        public string SourceType { get; set; }
        public string SourceNo { get; set; }
        public int DemandCount { get; set; }
    }
}
