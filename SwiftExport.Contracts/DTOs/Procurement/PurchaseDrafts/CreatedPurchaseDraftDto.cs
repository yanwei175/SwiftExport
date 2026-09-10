using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>已创建或已追加采购草稿的结果摘要。</summary>
    public class CreatedPurchaseDraftDto
    {
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public string OrderNo { get; set; }
        public Guid SupplierId { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public bool AppendedToExisting { get; set; }
    }
}
