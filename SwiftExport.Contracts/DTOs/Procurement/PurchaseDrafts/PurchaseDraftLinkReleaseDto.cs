using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购草稿减量预览中的需求贡献释放明细。</summary>
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
}
