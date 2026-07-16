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
    public Guid NetDemandId { get; set; }
    public int PlanRevision { get; set; }
    public int Sequence { get; set; }
    public NetDemandAllocationLifecycleStatusEnum LifecycleStatus { get; set; } = NetDemandAllocationLifecycleStatusEnum.Active;
    public DemandExecutionTypeEnum ExecutionType { get; set; }
    public decimal PlannedQty { get; set; }
    public DateTime? DemandRequiredDate { get; set; }
    public DateTime? PlannedDeliveryDate { get; set; }
    public Guid? PurchaseSupplierId { get; set; }
    public string? SupplierCodeSnapshot { get; set; }
    public string? SupplierNameSnapshot { get; set; }
    public Guid? DeliveryTargetAllocationId { get; set; }
    public Guid? DeliveryTargetSupplierId { get; set; }
    public string? DeliverySupplierCodeSnapshot { get; set; }
    public string? DeliverySupplierNameSnapshot { get; set; }
    public SupplierAssignmentSourceEnum AssignmentSource { get; set; }
    public Guid? ReplacesAllocationId { get; set; }
    public string? ChangeReason { get; set; }
    public string? CancelReasonCode { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancelledReason { get; set; }
}
