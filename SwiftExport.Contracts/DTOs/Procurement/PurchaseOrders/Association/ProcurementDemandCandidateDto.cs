using System;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>已有采购行关联候选；单位来自物料需求快照，未知不补默认值。</summary>
    public sealed class ProcurementDemandCandidateDto
    {
        public Guid NetDemandId { get; set; }
        public int Version { get; set; }
        public Guid GrossDemandId { get; set; }
        /// <summary>当前 GrossDemand.Version，用于关联并发校验；不是原销售或售后单的 SourceVersion。</summary>
        public int SourceDemandVersion { get; set; }
        public int DecompositionRevision { get; set; }
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public int? SourceLineNumber { get; set; }
        public Guid? ProductId { get; set; }
        public bool IsNonStandard { get; set; }
        public string Sku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public string MaterialType { get; set; }
        public string SupplyType { get; set; }
        public string Unit { get; set; }
        public Guid? DeliveryTargetNetDemandId { get; set; }
        public decimal NetQty { get; set; }
        public decimal LinkedQty { get; set; }
        public decimal RemainingQty { get; set; }
    }
}
