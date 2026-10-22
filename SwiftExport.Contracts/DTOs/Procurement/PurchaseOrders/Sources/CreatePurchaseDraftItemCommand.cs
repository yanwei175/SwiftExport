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
        /// <summary>本次采购数量，必须为正数且最多四位小数；提交时按当前有效占用复核可创建数量。</summary>
        public decimal PurchaseQty { get; set; }
        public Guid SupplierId { get; set; }
        public Guid? DeliveryTargetSupplierId { get; set; }
        /// <summary>本次采购采用的未税单价，允许零价，非负且最多四位小数。</summary>
        public decimal UnitPrice { get; set; }
        /// <summary>本次采购采用的含税单价，允许零价，非负且最多四位小数。</summary>
        public decimal TaxPrice { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
    }
}
