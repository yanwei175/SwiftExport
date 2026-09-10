using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购草稿批量创建结果。</summary>
    public class CreatePurchaseDraftsResult
    {
        public Guid ExecutionRequestId { get; set; }
        public List<CreatePurchaseDraftSourceResult> Sources { get; set; } = new List<CreatePurchaseDraftSourceResult>();
    }
}
