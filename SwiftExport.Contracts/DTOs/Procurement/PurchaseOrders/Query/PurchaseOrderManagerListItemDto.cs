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
        /// <summary>同单位且全部有效行可核实时的累计货好合计。</summary>
        public decimal? ReadyQty { get; set; }
        /// <summary>同单位且全部有效行可核实时的采购数量合计。</summary>
        public decimal? TotalQty { get; set; }
        /// <summary>同单位且全部有效行可核实时的正式净收货进度。</summary>
        public decimal? ReceiptProgress { get; set; }
        public string UnitSnapshot { get; set; }
        public PurchaseReadyStatusEnum ReadyStatus { get; set; }
        public PurchaseReceiptStatusEnum ReceiptStatus { get; set; }
        public int DueHitLineCount { get; set; }
        public DateTime? EarliestDueHitDate { get; set; }
        public bool HasUnconfirmedReadyRisk { get; set; }
        public string ReviewReason { get; set; }
        public decimal TaxTotalAmount { get; set; }
        public int ReportedLineCount { get; set; }
        public DateTime? ExpectedCompletionDate { get; set; }
        /// <summary>当前是否允许删除整张草稿；服务端执行时仍需持锁复核。</summary>
        public bool CanDeleteDraft { get; set; }
        /// <summary>不能删除草稿的原因，不泄露无权读取的下游信息。</summary>
        public string DeleteDraftBlockReason { get; set; }
        public bool CanConfirm { get; set; }
        public bool CanCancelConfirmation { get; set; }
        public string CommandBlockedReason { get; set; }
    }
}
