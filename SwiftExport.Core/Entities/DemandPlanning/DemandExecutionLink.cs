using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.DemandPlanning;

/// <summary>净需求到采购、生产或外协执行行的权威追溯关系。</summary>
[Table("DemandExecutionLink")]
public sealed class DemandExecutionLink : BaseEntity
{
    /// <summary>关联的净需求 Uid。</summary>
    public Guid NetDemandId { get; set; }
    /// <summary>历史计划分配 Uid；新执行关系直接以 NetDemandId 为业务锚点。</summary>
    public Guid? NetDemandAllocationId { get; set; }
    /// <summary>一次采购草稿创建请求的幂等标识。</summary>
    public Guid ExecutionRequestId { get; set; }
    /// <summary>请求内单条贡献的幂等标识，防止重复生成 Link。</summary>
    public Guid CommandItemId { get; set; }
    /// <summary>下游执行对象类型，例如采购单行。</summary>
    public string TargetType { get; set; } = string.Empty;
    /// <summary>下游单据头 Uid。</summary>
    public Guid TargetHeadId { get; set; }
    /// <summary>下游单号快照，供人工查账。</summary>
    public string TargetNo { get; set; } = string.Empty;
    /// <summary>下游执行明细行 Uid。</summary>
    public Guid TargetLineId { get; set; }
    /// <summary>追溯关系生命周期；取消采购草稿时 Link 会转为取消。</summary>
    public DemandExecutionLinkStatusEnum LinkStatus { get; set; } = DemandExecutionLinkStatusEnum.Active;
    /// <summary>最初分配给下游执行行的数量。</summary>
    public decimal AllocatedQty { get; set; }
    /// <summary>取消或减量累计释放数量；有效贡献为 AllocatedQty-ReleasedQty。</summary>
    public decimal ReleasedQty { get; set; }
    /// <summary>建立 Link 时保留的原始需求交期。</summary>
    public DateTime? DemandRequiredDate { get; set; }
    /// <summary>建立 Link 时保留的计划交付日期。</summary>
    public DateTime? PlannedDeliveryDate { get; set; }
    /// <summary>Link 取消时间。</summary>
    public DateTime? CancelledAt { get; set; }
    /// <summary>Link 取消操作人。</summary>
    public string? CancelledBy { get; set; }
    /// <summary>Link 取消原因。</summary>
    public string? CancelledReason { get; set; }
    /// <summary>执行关系反向处理时间；用于未来正式单据反向流程。</summary>
    public DateTime? ReversedAt { get; set; }
    /// <summary>执行关系反向处理操作人。</summary>
    public string? ReversedBy { get; set; }
    /// <summary>执行关系反向处理原因。</summary>
    public string? ReversedReason { get; set; }
    /// <summary>执行追溯备注。</summary>
    public string? Remark { get; set; }
}
