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
        /// <summary>仅新建采购单时采用的可编辑单头快照；追加既有草稿时忽略。</summary>
        public CreatePurchaseDraftHeaderCommand Header { get; set; }
    }
}
