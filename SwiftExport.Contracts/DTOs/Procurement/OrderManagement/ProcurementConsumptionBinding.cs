using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>一个采购行的一个完整客供件 BOM 路径所使用的消耗仓库，不等同于收货目标。</summary>
    public sealed class ProcurementConsumptionBinding
    {
        public Guid PurchaseOrderLineId { get; set; }
        public string Path { get; set; }
        public Guid WarehouseSiteId { get; set; }
    }
}
