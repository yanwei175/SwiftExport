using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress
{
    /// <summary>追加供应商累计进度的命令；不接受客户端跟进时间或操作人。</summary>
    public sealed class ReportPurchaseOrderProgressCommand
    {
        public Guid CommandId { get; set; }
        public Guid PurchaseOrderLineId { get; set; }
        public int ExpectedPurchaseOrderVersion { get; set; }
        public int ExpectedLineVersion { get; set; }
        public SupplierArrangementStatusEnum ArrangementStatus { get; set; }
        public decimal ReadyQty { get; set; }
        public DateTime? ExpectedReadyDate { get; set; }
        public string Note { get; set; }
        public string CorrectionReason { get; set; }
    }
}
