using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    public class CancelPurchaseDraftCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; }
    }

    public class PurchaseDraftReleasePreviewRequest
    {
        public int ExpectedVersion { get; set; }
        public decimal NewTotalQty { get; set; }
        public string Reason { get; set; }
    }

    public class ReleasePurchaseDraftCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public decimal NewTotalQty { get; set; }
        public string Reason { get; set; }
        public string PreviewToken { get; set; }
    }

    public class PurchaseDraftReleasePreviewResponse
    {
        public Guid PurchaseOrderId { get; set; }
        public int ExpectedVersion { get; set; }
        public decimal CurrentTotalQty { get; set; }
        public decimal NewTotalQty { get; set; }
        public decimal ReleaseQty { get; set; }
        public string PreviewToken { get; set; }
        public bool HasBlockers { get; set; }
        public List<string> Blockers { get; set; } = new List<string>();
        public List<PurchaseDraftLinkReleaseDto> Releases { get; set; } = new List<PurchaseDraftLinkReleaseDto>();
    }

    public class PurchaseDraftLinkReleaseDto
    {
        public Guid LinkId { get; set; }
        public Guid NetDemandId { get; set; }
        public Guid? HistoricalAllocationId { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public int? SourceLineNumber { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public decimal CurrentEffectiveQty { get; set; }
        public decimal ReleaseQty { get; set; }
        public decimal RemainingEffectiveQty { get; set; }
    }

    /// <remarks>来源表：DemandExecutionLink、NetDemand、PurchaseOrderHead、PurchaseOrderLine。</remarks>
    public class PurchaseDraftLifecycleLinkFactDto
    {
        public Guid LinkId { get; set; }
        public Guid NetDemandId { get; set; }
        public Guid? HistoricalAllocationId { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public int? SourceLineNumber { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public decimal AllocatedQty { get; set; }
        public decimal ReleasedQty { get; set; }
        public decimal EffectiveQty { get; set; }
    }

    public class PurchaseDraftLifecycleResult
    {
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        public NetDemandCommandOutcomeEnum Outcome { get; set; }
        public decimal ReleasedQty { get; set; }
        public string Message { get; set; }
    }
}
