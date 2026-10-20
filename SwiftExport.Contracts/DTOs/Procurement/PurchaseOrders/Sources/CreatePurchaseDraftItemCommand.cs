using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>采购草稿创建命令中的物料需求项。</summary>
    public class CreatePurchaseDraftItemCommand
    {
        public Guid CommandItemId { get; set; }
        public Guid NetDemandId { get; set; }
        public int ExpectedVersion { get; set; }
        public decimal PurchaseQty { get; set; }
        public Guid SupplierId { get; set; }
        public Guid? DeliveryTargetSupplierId { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TaxPrice { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
    }
}
