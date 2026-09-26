using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>采购反向资格预览；删行必须明确选定行及版本，整单操作不传行集合。</summary>
    public sealed class ProcurementReversalPreviewRequest
    {
        public int ExpectedVersion { get; set; }
        public List<ProcurementLineVersionDto> Lines { get; set; } = new List<ProcurementLineVersionDto>();
    }
}
