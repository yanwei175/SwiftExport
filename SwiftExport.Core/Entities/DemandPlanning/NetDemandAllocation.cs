using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.DemandPlanning;

/// <summary>
/// 净需求的计划执行分配。业务调整通过新增替代记录保留历史，不覆盖既有计划。
/// </summary>
[Table("NetDemandAllocation")]
public sealed class NetDemandAllocation : BaseEntity
{
    /// <summary>被计划分配的净需求 Uid。</summary>
    public Guid NetDemandId { get; set; }
    /// <summary>计划修订号；修改计划时新增下一版本，不覆盖历史记录。</summary>
    public int PlanRevision { get; set; }
    /// <summary>同一净需求、同一计划版本内的序号，为未来拆分多条计划预留。</summary>
    public int Sequence { get; set; }
    /// <summary>计划记录生命周期；同一净需求首期只允许一条活动计划。</summary>
    public NetDemandAllocationLifecycleStatusEnum LifecycleStatus { get; set; } = NetDemandAllocationLifecycleStatusEnum.Active;
    /// <summary>执行方式：采购、生产或外协。</summary>
    public DemandExecutionTypeEnum ExecutionType { get; set; }
    /// <summary>本计划承担的执行数量。</summary>
    public decimal PlannedQty { get; set; }
    /// <summary>净需求的原始业务交期快照，供计划对比使用。</summary>
    public DateTime? DemandRequiredDate { get; set; }
    /// <summary>实际计划交付日期，可由计划人员调整。</summary>
    public DateTime? PlannedDeliveryDate { get; set; }
    /// <summary>采购执行供应商 Uid；非采购方式或待分配时可以为空。</summary>
    public Guid? PurchaseSupplierId { get; set; }
    /// <summary>计划成立时的执行供应商编码快照。</summary>
    public string? SupplierCodeSnapshot { get; set; }
    /// <summary>计划成立时的执行供应商名称快照。</summary>
    public string? SupplierNameSnapshot { get; set; }
    /// <summary>送达目标计划 Uid；客供件指向父件的活动计划。</summary>
    public Guid? DeliveryTargetAllocationId { get; set; }
    /// <summary>送达目标供应商 Uid，由目标计划的执行供应商推导。</summary>
    public Guid? DeliveryTargetSupplierId { get; set; }
    /// <summary>送达目标供应商编码快照。</summary>
    public string? DeliverySupplierCodeSnapshot { get; set; }
    /// <summary>送达目标供应商名称快照。</summary>
    public string? DeliverySupplierNameSnapshot { get; set; }
    /// <summary>供应商分配来源，例如产品默认、待分配、人工或重新分配。</summary>
    public SupplierAssignmentSourceEnum AssignmentSource { get; set; }
    /// <summary>被当前计划替代的上一条 Allocation Uid。</summary>
    public Guid? ReplacesAllocationId { get; set; }
    /// <summary>计划替代或人工调整原因。</summary>
    public string? ChangeReason { get; set; }
    /// <summary>机器可识别的取消原因代码。</summary>
    public string? CancelReasonCode { get; set; }
    /// <summary>计划取消时间。</summary>
    public DateTime? CancelledAt { get; set; }
    /// <summary>计划取消操作人。</summary>
    public string? CancelledBy { get; set; }
    /// <summary>计划取消的业务原因。</summary>
    public string? CancelledReason { get; set; }
}
