using System;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace
{
    /// <summary>采购行对应的单条需求贡献及来源身份，保留释放历史。</summary>
    public sealed class PurchaseOrderTraceLinkDto
    {
        public Guid LinkId { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public Guid NetDemandId { get; set; }
        public Guid? GrossDemandId { get; set; }
        public DemandExecutionLinkStatusEnum LinkStatus { get; set; }
        public decimal AllocatedQty { get; set; }
        public decimal ReleasedQty { get; set; }
        public decimal EffectiveQty { get; set; }
        public string SourceType { get; set; }
        public Guid? SourceUid { get; set; }
        public Guid? SourceLineId { get; set; }
        public string SourceNo { get; set; }
        public int? SourceLineNumber { get; set; }
        public int DecompositionRevision { get; set; }
        public int? SourceVersion { get; set; }
        public int? SourceDemandVersionSnapshot { get; set; }
        public int? SourceVersionSnapshot { get; set; }
        public string DemandBasisFingerprint { get; set; }
        public string DemandBasisFingerprintSnapshot { get; set; }
        public string UnitSnapshot { get; set; }
        public DateTime? DemandRequiredDate { get; set; }
        public DateTime? CancelledAt { get; set; }
        public bool IsDeleted { get; set; }
        public string CancelledBy { get; set; }
        public string CancelledReason { get; set; }
        public bool NeedsReview { get; set; }
        public string ReviewReason { get; set; }
    }
}
