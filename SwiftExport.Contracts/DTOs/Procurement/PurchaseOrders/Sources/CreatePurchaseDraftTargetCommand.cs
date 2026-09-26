using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购草稿创建时的目标单据选择。</summary>
    public class CreatePurchaseDraftTargetCommand
    {
        public Guid SupplierId { get; set; }
        public PurchaseDraftTargetModeEnum TargetMode { get; set; }
        public Guid? ExistingPurchaseOrderId { get; set; }
        public int? ExpectedPurchaseOrderVersion { get; set; }
    }
}
