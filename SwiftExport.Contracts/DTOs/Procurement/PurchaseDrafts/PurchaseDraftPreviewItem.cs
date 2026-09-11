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
        /// <summary>本次采购明确采用的未税价；客户端将缺省主数据价规范化为零。</summary>
        public decimal UnitPrice { get; set; }
        /// <summary>本次采购明确采用的含税价；客户端将缺省主数据价规范化为零。</summary>
        public decimal TaxPrice { get; set; }
        /// <summary>采购员本次选择的计划交期；允许为空。</summary>
        public DateTime? PlannedDeliveryDate { get; set; }
    }
}
