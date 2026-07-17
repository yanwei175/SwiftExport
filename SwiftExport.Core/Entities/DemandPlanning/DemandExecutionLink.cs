using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.DemandPlanning;

/// <summary>净需求到采购、生产或外协执行行的权威追溯关系。</summary>
[Table("DemandExecutionLink")]
public sealed class DemandExecutionLink : BaseEntity
{
    /// <summary>兼容期由专用 SQL 映射到物理列 DependentDemandId。</summary>
    public Guid NetDemandId { get; set; }
    public Guid? NetDemandAllocationId { get; set; }
    public Guid? ExecutionRequestId { get; set; }
    public Guid? CommandItemId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetHeadId { get; set; }
    public string TargetNo { get; set; } = string.Empty;
    public Guid TargetLineId { get; set; }
    public DemandExecutionLinkStatusEnum LinkStatus { get; set; } = DemandExecutionLinkStatusEnum.Active;
    public decimal AllocatedQty { get; set; }
    /// <summary>取消或减量累计释放数量；有效贡献为 AllocatedQty-ReleasedQty。</summary>
    public decimal ReleasedQty { get; set; }
    public decimal CompletedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public DateTime? DemandRequiredDate { get; set; }
    public DateTime? PlannedDeliveryDate { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancelledReason { get; set; }
    public DateTime? ReversedAt { get; set; }
    public string? ReversedBy { get; set; }
    public string? ReversedReason { get; set; }
    public string? Remark { get; set; }
}
