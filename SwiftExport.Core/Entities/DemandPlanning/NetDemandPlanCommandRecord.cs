using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.DemandPlanning;

/// <summary>净需求计划命令的持久幂等结果。</summary>
[Table("NetDemandPlanCommandLog")]
public sealed class NetDemandPlanCommandRecord : BaseEntity
{
    public Guid CommandId { get; set; }
    public Guid OriginalAllocationId { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public NetDemandCommandOutcomeEnum Outcome { get; set; }
    public Guid? NewAllocationId { get; set; }
    public int NewAllocationVersion { get; set; }
    public string? ResultMessage { get; set; }
    public string? AffectedAllocationIds { get; set; }
}
