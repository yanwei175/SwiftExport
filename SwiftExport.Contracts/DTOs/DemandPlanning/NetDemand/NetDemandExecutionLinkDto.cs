using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>物料对采购执行的历史关联投影，供采购承接及安全回退使用。</summary>
    /// <remarks>来源表：DemandExecutionLink、PurchaseOrderHead、PurchaseOrderLine。</remarks>
    public class NetDemandExecutionLinkDto
    {
        public Guid Uid { get; set; }
        public Guid NetDemandId { get; set; }
        public Guid? NetDemandAllocationId { get; set; }
        public DemandExecutionLinkStatusEnum LinkStatus { get; set; }
        public string TargetType { get; set; }
        public Guid TargetHeadId { get; set; }
        public int? TargetHeadVersion { get; set; }
        public PurchaseOrderLifecycleStatusEnum? TargetHeadLifecycleStatus { get; set; }
        public ConfirmationStatusEnum? TargetHeadConfirmationStatus { get; set; }
        public string TargetNo { get; set; }
        public Guid TargetLineId { get; set; }
        public int? TargetLineNumber { get; set; }
        public decimal AllocatedQty { get; set; }
        public decimal CompletedQty { get; set; }
        public decimal ReceivedQty { get; set; }
        public DateTime? DemandRequiredDate { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string CancelledBy { get; set; }
        public string CancelledReason { get; set; }
        public string Remark { get; set; }
    }

}
