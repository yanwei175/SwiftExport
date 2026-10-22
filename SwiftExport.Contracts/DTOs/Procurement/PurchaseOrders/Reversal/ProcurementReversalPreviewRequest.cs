using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>采购反向资格预览；删行传选定行及版本，删除整张草稿传全部有效行及版本，取消确认和作废不传行集合。</summary>
    public sealed class ProcurementReversalPreviewRequest
    {
        public int ExpectedVersion { get; set; }
        public List<ProcurementLineVersionDto> Lines { get; set; } = new List<ProcurementLineVersionDto>();
    }
}
