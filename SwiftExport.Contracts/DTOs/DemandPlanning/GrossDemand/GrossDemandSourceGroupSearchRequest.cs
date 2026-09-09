using SwiftExport.Contracts.SysEnums;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    public class GrossDemandSourceGroupSearchRequest
    {
        public List<GrossDemandLifecycleStatusEnum> LifecycleStatuses { get; set; } = new List<GrossDemandLifecycleStatusEnum>();
    }
}
