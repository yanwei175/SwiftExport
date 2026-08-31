using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>已确认采购行的依据版本、无 BOM 标记及节点历史快照。</summary>
    public sealed class ProcurementConfirmedLineDto
    {
        public Guid PurchaseOrderLineId { get; set; }
        public Guid RootProductId { get; set; }
        public int LineVersion { get; set; }
        public bool HasBom { get; set; }
        public string Fingerprint { get; set; }
        public string RatioPolicy { get; set; }
        public DateTime ConfirmedAt { get; set; }
        public List<ProcurementConfirmedNodeDto> Nodes { get; set; } = new List<ProcurementConfirmedNodeDto>();
    }
}
