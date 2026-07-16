using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    public class ReplaceNetDemandPlanCommand
    {
        public Guid CommandId { get; set; }
        public Guid AllocationId { get; set; }
        public int ExpectedVersion { get; set; }
        public DemandExecutionTypeEnum ExecutionType { get; set; }
        public Guid? PurchaseSupplierId { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public string Reason { get; set; }
    }

    public class ApplyProductDefaultSupplierCommand
    {
        public Guid CommandId { get; set; }
        public Guid AllocationId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; }
    }

    public class BatchAssignSupplierCommand
    {
        public Guid CommandId { get; set; }
        public Guid SupplierId { get; set; }
        public string Reason { get; set; }
        public List<BatchAssignSupplierItem> Items { get; set; } = new List<BatchAssignSupplierItem>();
    }

    public class BatchAssignSupplierItem
    {
        public Guid AllocationId { get; set; }
        public int ExpectedVersion { get; set; }
        public string ClientKey { get; set; }
        public int RowIndex { get; set; }
    }

    public class NetDemandPlanCommandResult
    {
        public List<NetDemandPlanCommandItemResult> Items { get; set; } = new List<NetDemandPlanCommandItemResult>();
    }

    public class NetDemandPlanCommandItemResult
    {
        public Guid AllocationId { get; set; }
        public Guid? NewAllocationId { get; set; }
        public int NewAllocationVersion { get; set; }
        public string ClientKey { get; set; }
        public int RowIndex { get; set; }
        public NetDemandCommandOutcomeEnum Outcome { get; set; }
        public string ErrorCode { get; set; }
        public string Message { get; set; }
        public List<Guid> AffectedCustomerSuppliedAllocationIds { get; set; } = new List<Guid>();
        public List<NetDemandExecutionLinkDto> Blockers { get; set; } = new List<NetDemandExecutionLinkDto>();
    }
}
