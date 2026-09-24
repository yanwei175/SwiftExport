using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress
{
    /// <summary>供应商累计进度的一次不可覆盖反馈；时间和人员由服务端形成。</summary>
    public sealed class SupplierProgressReportDto
    {
        public Guid Uid { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public int Sequence { get; set; }
        public SupplierArrangementStatusEnum ArrangementStatus { get; set; }
        /// <summary>本次报告形成后的累计数量快照；空值表示至今尚未反馈数量。</summary>
        public decimal? ReadyQty { get; set; }
        public DateTime? ExpectedReadyDate { get; set; }
        public DateTime SupplierReportedDate { get; set; }
        public string Note { get; set; }
        public bool IsCorrection { get; set; }
        public string CorrectionReason { get; set; }
        public Guid CommandId { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
    }
}
