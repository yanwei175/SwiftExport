using System;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto
{
    /// <summary>采购管理页的新链头表摘要；不承载完整跟进历史、Trace、入库单或库存流水。</summary>
    public sealed class PurchaseOrderManagerListItemDto
    {
        public Guid PurchaseOrderId { get; set; }
        public int Version { get; set; }
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        public string OrderNo { get; set; }
        public string BoNo { get; set; }
        /// <summary>采购头保存的客户代码快照，用于管理列表识别与筛选。</summary>
        public string CustomerCode { get; set; }
        public string SupplierName { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public DateTime? OrderDate { get; set; }
        public DateTime? ExpectedDate { get; set; }
        public DateTime? ReplyDate { get; set; }
        public int LineCount { get; set; }
        public decimal ReadyQty { get; set; }
        public decimal TotalQty { get; set; }
        public decimal ReceiptProgress { get; set; }
        public decimal TaxTotalAmount { get; set; }
        public int ReportedLineCount { get; set; }
        public DateTime? ExpectedCompletionDate { get; set; }
        public bool CanConfirm { get; set; }
        public bool CanCancelConfirmation { get; set; }
        public string CommandBlockedReason { get; set; }
    }
}
