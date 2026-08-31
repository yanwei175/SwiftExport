using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.Products.Execution;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>单个采购行当前有效的完整 BOM 结构依据。</summary>
    public sealed class ProcurementLineConfirmationContextDto
    {
        public Guid PurchaseOrderLineId { get; set; }
        public int LineVersion { get; set; }
        public BomExecutionSnapshotDto Bom { get; set; }
    }
}
