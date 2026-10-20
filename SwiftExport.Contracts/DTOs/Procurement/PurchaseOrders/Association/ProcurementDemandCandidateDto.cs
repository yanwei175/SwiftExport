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
        /// <summary>可信已确认采购关联量；无权或证据异常为空。</summary>
        public decimal? PurchasedQty { get; set; }
        /// <summary>可信未确认草稿的来源占用量，不计入已采购；无权或证据异常为空。</summary>
        public decimal? DraftOccupiedQty { get; set; }
        /// <summary>需求量减已采购量及草稿占用量；证据异常为空，不得按零放行。</summary>
        public decimal? CreatableQty { get; set; }
        /// <summary>需求量减已采购量；草稿不减少此数量，无权或证据异常为空。</summary>
        public decimal? UnpurchasedQty { get; set; }
        /// <summary>来源、占用目标或数量无法核实的具体原因；非空时不得创建或补关联。</summary>
        public string ReviewReason { get; set; }
        public decimal LinkedQty { get; set; }
        public decimal RemainingQty { get; set; }
    }
}
