using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>采购行商务编辑及乐观并发版本。</summary>
    public sealed class SaveProcurementOrderLineCommand
    {
        public Guid PurchaseOrderLineId { get; set; }
        public int ExpectedVersion { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public string SupplierSku { get; set; }
        public string DescriptionZh { get; set; }
        public string SubNote { get; set; }
    }
}
