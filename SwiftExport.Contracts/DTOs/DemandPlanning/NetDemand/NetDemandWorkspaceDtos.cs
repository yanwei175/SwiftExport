using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>
    /// 净需求工作台只读行。选择、脏状态和本次转单数量由客户端模型维护。
    /// </summary>
    public class NetDemandListItemDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public NetDemandLifecycleStatusEnum LifecycleStatus { get; set; }
        public Guid GrossDemandId { get; set; }
        public int DecompositionRevision { get; set; }
        public int LineNumber { get; set; }

        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public Guid SourceLineId { get; set; }
        public int? SourceLineNumber { get; set; }

        public Guid? ParentNetDemandId { get; set; }
        public Guid RootNetDemandId { get; set; }
        public string RootSku { get; set; }
        public bool IsCustomerSupplied { get; set; }
        public DemandDeliveryModeEnum DeliveryMode { get; set; }

        public Guid? ProductId { get; set; }
        public bool IsNonStandard { get; set; }
        public string Sku { get; set; }
        public string LinkSku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public string Color { get; set; }
        public string Specification { get; set; }
        public string ImagePath { get; set; }
        public string MaterialType { get; set; }
        public string SupplyType { get; set; }

        public decimal GrossQty { get; set; }
        public decimal NetQty { get; set; }
        public DateTime? DemandRequiredDate { get; set; }

        public Guid AllocationId { get; set; }
        public int AllocationVersion { get; set; }
        public int PlanRevision { get; set; }
        public int AllocationSequence { get; set; }
        public NetDemandAllocationLifecycleStatusEnum AllocationLifecycleStatus { get; set; }
        public DemandExecutionTypeEnum ExecutionType { get; set; }
        public SupplierAssignmentSourceEnum AssignmentSource { get; set; }
        public decimal PlannedQty { get; set; }
        public decimal AllocatedQty { get; set; }
        public decimal ReleasedQty { get; set; }
        public decimal EffectiveQty { get; set; }
        public decimal RemainingQty { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }

        public Guid? PurchaseSupplierId { get; set; }
        public string PurchaseSupplierCode { get; set; }
        public string PurchaseSupplierName { get; set; }
        public string CurrentPurchaseSupplierName { get; set; }
        public Guid? DeliveryTargetAllocationId { get; set; }
        public Guid? DeliveryTargetSupplierId { get; set; }
        public string DeliveryTargetSupplierCode { get; set; }
        public string DeliveryTargetSupplierName { get; set; }

        public Guid? DefaultSupplierId { get; set; }
        public string DefaultSupplierCode { get; set; }
        public string DefaultSupplierName { get; set; }
        public bool DefaultSupplierChanged { get; set; }

        public NetDemandPlanStatusEnum PlanStatus { get; set; }
        public string PurchaseOrderNos { get; set; }
        public string BlockerSummary { get; set; }
        public bool CanReplacePlan { get; set; }
        public bool CanApplyDefaultSupplier { get; set; }
        public bool CanTransferPurchase { get; set; }

        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string UpdatedBy { get; set; }
    }

    public class NetDemandAllocationDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public Guid NetDemandId { get; set; }
        public int PlanRevision { get; set; }
        public int Sequence { get; set; }
        public NetDemandAllocationLifecycleStatusEnum LifecycleStatus { get; set; }
        public DemandExecutionTypeEnum ExecutionType { get; set; }
        public SupplierAssignmentSourceEnum AssignmentSource { get; set; }
        public decimal PlannedQty { get; set; }
        public DateTime? DemandRequiredDate { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public Guid? PurchaseSupplierId { get; set; }
        public string PurchaseSupplierCode { get; set; }
        public string PurchaseSupplierName { get; set; }
        public Guid? DeliveryTargetAllocationId { get; set; }
        public Guid? DeliveryTargetSupplierId { get; set; }
        public string DeliveryTargetSupplierCode { get; set; }
        public string DeliveryTargetSupplierName { get; set; }
        public Guid? ReplacesAllocationId { get; set; }
        public string ChangeReason { get; set; }
        public string CancelReasonCode { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string CancelledBy { get; set; }
        public string CancelledReason { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
    }

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

    public class NetDemandDetailDto
    {
        public NetDemandListItemDto Summary { get; set; }
        public NetDemandAllocationDto ActiveAllocation { get; set; }
        public List<NetDemandListItemDto> RelatedCustomerSuppliedPlans { get; set; } = new List<NetDemandListItemDto>();
        public List<NetDemandExecutionLinkDto> BlockingExecutionLinks { get; set; } = new List<NetDemandExecutionLinkDto>();
    }

    public class NetDemandTraceDto
    {
        public NetDemandListItemDto Summary { get; set; }
        public List<NetDemandAllocationDto> AllocationHistory { get; set; } = new List<NetDemandAllocationDto>();
        public List<NetDemandExecutionLinkDto> ExecutionLinks { get; set; } = new List<NetDemandExecutionLinkDto>();
    }
}
