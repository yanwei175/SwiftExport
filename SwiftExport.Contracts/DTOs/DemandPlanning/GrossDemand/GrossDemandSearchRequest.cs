using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    public class GrossDemandSearchRequest : PagedRequestBase
    {
        public List<string> SourceTypes { get; set; } = new List<string>();
        public List<string> SourceNos { get; set; } = new List<string>();
        public List<string> Skus { get; set; } = new List<string>();
        public List<string> CustomerSkus { get; set; } = new List<string>();
        public List<GrossDemandLifecycleStatusEnum> LifecycleStatuses { get; set; } = new List<GrossDemandLifecycleStatusEnum>();
        public List<GrossDemandDecompositionStatusEnum> DecompositionStatuses { get; set; } = new List<GrossDemandDecompositionStatusEnum>();
        public List<DemandExecutionProgressEnum> ExecutionProgresses { get; set; } = new List<DemandExecutionProgressEnum>();
        public DateTime? RequiredDateFrom { get; set; }
        public DateTime? RequiredDateTo { get; set; }
        public bool? IsNonStandard { get; set; }
    }
}
