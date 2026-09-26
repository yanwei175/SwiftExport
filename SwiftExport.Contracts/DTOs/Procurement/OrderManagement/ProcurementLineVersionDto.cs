using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>订单命令完成时的采购行版本。</summary>
    public sealed class ProcurementLineVersionDto
    {
        public Guid PurchaseOrderLineId { get; set; }
        public int Version { get; set; }
    }
}
