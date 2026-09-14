using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress
{
    /// <summary>采购跟进详情；组合指定采购单的行摘要与各行追加式反馈历史。</summary>
    public sealed class PurchaseOrderProgressDetailDto
    {
        public Guid PurchaseOrderId { get; set; }
        public List<PurchaseOrderProgressLineDto> Lines { get; set; } = new List<PurchaseOrderProgressLineDto>();
    }
}
