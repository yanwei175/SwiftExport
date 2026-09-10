using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购草稿取消或减量的生命周期结果。</summary>
    public class PurchaseDraftLifecycleResult
    {
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        public NetDemandCommandOutcomeEnum Outcome { get; set; }
        public decimal ReleasedQty { get; set; }
        public string Message { get; set; }
    }
}
