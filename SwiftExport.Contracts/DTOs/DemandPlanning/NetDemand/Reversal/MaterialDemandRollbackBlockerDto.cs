using System;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>物料需求安全回退的活动执行阻断明细。</summary>
    public class MaterialDemandRollbackBlockerDto
    {
        public Guid NetDemandId { get; set; }
        public string TargetType { get; set; }
        public string TargetNo { get; set; }
        public Guid TargetLineId { get; set; }
        public DemandExecutionLinkStatusEnum LinkStatus { get; set; }
        public string Message { get; set; }
    }
}
