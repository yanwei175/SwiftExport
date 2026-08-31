using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>未确认采购当前完整 BOM 依据及待绑定路径；指纹变化必须重新取得依据。</summary>
    public sealed class ProcurementConfirmationContextDto
    {
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public string Fingerprint { get; set; }
        public List<ProcurementLineConfirmationContextDto> Lines { get; set; } = new List<ProcurementLineConfirmationContextDto>();
    }
}
