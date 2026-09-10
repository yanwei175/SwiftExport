using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
/// <summary>按毛需求来源展开的采购追溯结果。</summary>
    /// <remarks>来源表：GrossDemand、NetDemand、DemandExecutionLink、PurchaseOrderHead。</remarks>
    public class SourcePurchaseTraceDto
    {
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public List<SourcePurchaseTraceLineDto> Lines { get; set; } = new List<SourcePurchaseTraceLineDto>();
    }
}

