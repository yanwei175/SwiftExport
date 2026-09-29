using System;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace
{
    /// <summary>供应商一次累计反馈或修正，不代表正式实收。</summary>
    public sealed class PurchaseOrderTraceProgressDto
    {
        public Guid Uid { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public int Sequence { get; set; }
        public SupplierArrangementStatusEnum ArrangementStatus { get; set; }
        public decimal? ReadyQty { get; set; }
        public DateTime? ExpectedReadyDate { get; set; }
        public DateTime SupplierReportedDate { get; set; }
        public string Note { get; set; }
        public bool IsCorrection { get; set; }
        public string CorrectionReason { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
    }
}
