using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.DemandPlanning;

/// <summary>净需求计划命令的持久幂等结果。</summary>
[Table("NetDemandPlanCommandLog")]
public sealed class NetDemandPlanCommandRecord : BaseEntity
{
    /// <summary>客户端提交的计划命令幂等 Uid。</summary>
    public Guid CommandId { get; set; }
    /// <summary>命令执行前被替代或处理的原活动 Allocation Uid。</summary>
    public Guid OriginalAllocationId { get; set; }
    /// <summary>命令类型，例如替代计划、应用产品默认供应商或批量分配。</summary>
    public string CommandType { get; set; } = string.Empty;
    /// <summary>规范化请求内容指纹，用于识别同一 CommandId 是否被换参重放。</summary>
    public string RequestFingerprint { get; set; } = string.Empty;
    /// <summary>命令执行结果，例如成功、已应用或失败。</summary>
    public NetDemandCommandOutcomeEnum Outcome { get; set; }
    /// <summary>命令成功后生成的新 Allocation Uid。</summary>
    public Guid? NewAllocationId { get; set; }
    /// <summary>新 Allocation 的乐观并发版本号。</summary>
    public int NewAllocationVersion { get; set; }
    /// <summary>首次执行时持久化的结果消息，重试时原样返回。</summary>
    public string? ResultMessage { get; set; }
    /// <summary>本命令影响的 Allocation Uid 集合序列化快照。</summary>
    public string? AffectedAllocationIds { get; set; }
}
