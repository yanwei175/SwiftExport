using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
/// <remarks>来源表：DemandExecutionLink、NetDemand、PurchaseOrderHead、PurchaseOrderLine。</remarks>
    /// <summary>采购草稿生命周期操作使用的需求贡献事实。</summary>
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
}
