using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>手工采购行输入；基础单位与产品身份快照由服务端读取。</summary>
    public sealed class CreateIndependentPurchaseOrderLineCommand
    {
        public Guid ProductId { get; set; }
        public decimal Quantity { get; set; }
        public Guid WarehouseSiteId { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? TaxPrice { get; set; }
        public DateTime? PlannedDeliveryDate { get; set; }
        public string SupplierSku { get; set; }
        public string DescriptionZh { get; set; }
        public string SubNote { get; set; }
    }
}
