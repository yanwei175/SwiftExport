using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.PurchaseOrders
{
    /// <summary>安全取消采购草稿；仅允许新净需求链路生成的活动待确认草稿。</summary>
    public class CancelPurchaseDraftCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>采购草稿减量预览请求。</summary>
    public class PurchaseDraftReleasePreviewRequest
    {
        public int ExpectedVersion { get; set; }
        public decimal NewTotalQty { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>确认减量命令；执行时服务端重新锁定并校验预览事实。</summary>
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

    /// <summary>按冻结顺序计算出的 Link 释放贡献。</summary>
    public class PurchaseDraftLinkReleaseDto
    {
        public Guid LinkId { get; set; }
        public Guid AllocationId { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public int? SourceLineNumber { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public decimal CurrentEffectiveQty { get; set; }
        public decimal ReleaseQty { get; set; }
        public decimal RemainingEffectiveQty { get; set; }
    }

    /// <summary>仓储提供给生命周期编排的锁定 Link 事实。</summary>
    public class PurchaseDraftLifecycleLinkFactDto
    {
        public Guid LinkId { get; set; }
        public Guid AllocationId { get; set; }
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
