using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
/// <summary>客户端选中的物料需求、并发版本及本次采购决策。</summary>
    public class PurchaseDraftPreviewItem
    {
        public Guid CommandItemId { get; set; }
        public Guid NetDemandId { get; set; }
        public int ExpectedVersion { get; set; }
        public decimal ExpectedRemainingQty { get; set; }
        public decimal PurchaseQty { get; set; }
        public Guid SupplierId { get; set; }
        public Guid? DeliveryTargetSupplierId { get; set; }
    }
}

