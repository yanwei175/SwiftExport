using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>可分配采购行快照及权威未关联量；历史未知保持空值并展示阻断原因。</summary>
    public sealed class ProcurementDemandAssociationLineDto
    {
        public Guid PurchaseOrderLineId { get; set; }
        public int Version { get; set; }
        public int LineNumber { get; set; }
        public Guid? ProductId { get; set; }
        public bool IsNonStandard { get; set; }
        public string Sku { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal? UnlinkedQty { get; set; }
        public decimal LinkedQty { get; set; }
        public string BlockReason { get; set; }
        public List<ProcurementDemandAssociationLinkDto> Links { get; set; } = new List<ProcurementDemandAssociationLinkDto>();
    }
}
