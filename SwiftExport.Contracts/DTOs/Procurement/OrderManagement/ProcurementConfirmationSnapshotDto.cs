using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>独立取得当前已冻结的确认依据，不读取今天的 BOM，也不塞入管理详情。</summary>
    public sealed class ProcurementConfirmationSnapshotDto
    {
        public Guid PurchaseOrderId { get; set; }
        public Guid ConfirmationId { get; set; }
        public List<ProcurementConfirmedLineDto> Lines { get; set; } = new List<ProcurementConfirmedLineDto>();
    }
}
