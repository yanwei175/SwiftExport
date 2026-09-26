using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
/// <summary>来源与供应商对应的采购草稿写入目标。</summary>
    /// <remarks>来源表：NetDemand、Product、Supplier。</remarks>
    public class PurchaseDraftTargetSelectionDto
    {
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public Guid SupplierId { get; set; }
        public PurchaseDraftTargetModeEnum TargetMode { get; set; }
        public Guid? ExistingPurchaseOrderId { get; set; }
        public int? ExpectedPurchaseOrderVersion { get; set; }
    }
}

