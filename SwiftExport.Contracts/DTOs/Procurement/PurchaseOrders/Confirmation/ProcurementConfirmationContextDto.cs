using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>待确认采购事实和头行版本；指纹变化必须重新取得预览，不包含 BOM 或仓库绑定。</summary>
    public sealed class ProcurementConfirmationContextDto
    {
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public string Fingerprint { get; set; }
        public List<ProcurementLineConfirmationContextDto> Lines { get; set; } = new List<ProcurementLineConfirmationContextDto>();
    }
}
