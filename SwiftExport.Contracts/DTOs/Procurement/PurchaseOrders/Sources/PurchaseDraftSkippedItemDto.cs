using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购草稿预览中被跳过的物料需求。</summary>
    public class PurchaseDraftSkippedItemDto
    {
        public Guid NetDemandId { get; set; }
        public DemandExecutionTypeEnum ExecutionType { get; set; }
        public string ReasonCode { get; set; }
        public string Message { get; set; }
    }
}
