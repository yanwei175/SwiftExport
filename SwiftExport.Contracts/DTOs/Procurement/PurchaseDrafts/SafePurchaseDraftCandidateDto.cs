using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
/// <remarks>来源表：NetDemand、GrossDemand、Product、Supplier、DemandExecutionLink。</remarks>
    /// <summary>可安全追加采购行的既有草稿候选。</summary>
    public class SafePurchaseDraftCandidateDto
    {
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public string OrderNo { get; set; }
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public Guid SupplierId { get; set; }
    }
}
