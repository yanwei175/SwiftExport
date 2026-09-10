using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>单个来源的采购草稿创建结果。</summary>
    public class CreatePurchaseDraftSourceResult
    {
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public NetDemandCommandOutcomeEnum Outcome { get; set; }
        public string ErrorCode { get; set; }
        public string Message { get; set; }
        public List<CreatedPurchaseDraftDto> PurchaseDrafts { get; set; } = new List<CreatedPurchaseDraftDto>();
    }
}
