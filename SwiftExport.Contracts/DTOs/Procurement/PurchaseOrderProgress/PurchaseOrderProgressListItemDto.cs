using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress
{
    /// <summary>采购跟进工作台头表摘要；不承载详情编辑、Trace 或库存流水。</summary>
    public sealed class PurchaseOrderProgressListItemDto
    {
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        public string OrderNo { get; set; }
        public DateTime? OrderDate { get; set; }
        public string BoNo { get; set; }
        public string SupplierName { get; set; }
        public int LineCount { get; set; }
        public int ReportedLineCount { get; set; }
        public decimal TotalQty { get; set; }
        public decimal LatestReadyQty { get; set; }
        /// <summary>最新报告没有已知累计数量的采购行数，包括尚未跟进的行。</summary>
        public int UnknownReadyQtyLineCount { get; set; }
        public DateTime? LatestExpectedReadyDate { get; set; }
        public string DemandSourceNo { get; set; }
        public bool CanReportProgress { get; set; }
        public string ReadOnlyReason { get; set; }
    }
}
