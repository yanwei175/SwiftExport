using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders
{
    /// <summary>按采购单身份展示订单事实与履约摘要；无权维度为空，不承载生命周期写命令资格。</summary>
    public sealed class PurchaseOrderTrackingItemDto
    {
        /// <summary>采购单稳定身份。</summary>
        public Guid PurchaseOrderId { get; set; }
        /// <summary>订单读取时版本。</summary>
        public int Version { get; set; }
        /// <summary>采购单保存的来源类别。</summary>
        public int OriginKind { get; set; }
        /// <summary>系统采购单号。</summary>
        public string OrderNo { get; set; }
        /// <summary>自定义采购单号。</summary>
        public string BoNo { get; set; }
        /// <summary>客户代码快照。</summary>
        public string CustomerCode { get; set; }
        /// <summary>实际供应商显示名称。</summary>
        public string SupplierName { get; set; }
        /// <summary>采购单确认状态。</summary>
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        /// <summary>采购单生命周期。</summary>
        public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
        /// <summary>采购日期。</summary>
        public DateTime? OrderDate { get; set; }
        /// <summary>订单保存的预计交期。</summary>
        public DateTime? ExpectedDate { get; set; }
        /// <summary>订单保存的回复交期。</summary>
        public DateTime? ReplyDate { get; set; }
        /// <summary>全部有效采购行数，不是筛选命中数。</summary>
        public int LineCount { get; set; }
        /// <summary>同单位且可核实时的订单数量合计，混合单位不得相加。</summary>
        public decimal? TotalQty { get; set; }
        /// <summary>统一数量单位，混合或异常时为空。</summary>
        public string UnitSnapshot { get; set; }
        /// <summary>订单含税金额合计。</summary>
        public decimal TaxTotalAmount { get; set; }
        /// <summary>当前身份是否可读取供应商反馈维度。</summary>
        public bool CanViewProgress { get; set; }
        /// <summary>当前身份是否可读取正式收货维度。</summary>
        public bool CanViewReceipts { get; set; }
        /// <summary>同单位可信累计货好量，无权或无法核实时为空。</summary>
        public decimal? ReadyQty { get; set; }
        /// <summary>整单货好查询状态，无权时为空。</summary>
        public PurchaseReadyStatusEnum? ReadyStatus { get; set; }
        /// <summary>已反馈的有效行数，无权时为空。</summary>
        public int? ReportedLineCount { get; set; }
        /// <summary>供应商反馈的预计完成日期，不覆盖订单约定交期。</summary>
        public DateTime? ExpectedCompletionDate { get; set; }
        /// <summary>同单位可信正式净收货进度，无权或证据异常时为空。</summary>
        public decimal? ReceiptProgress { get; set; }
        /// <summary>整单正式收货状态，无权时为空。</summary>
        public PurchaseReceiptStatusEnum? ReceiptStatus { get; set; }
        /// <summary>当前交期风险条件命中的行数，无权时为空。</summary>
        public int? DueHitLineCount { get; set; }
        /// <summary>风险命中行最早计划交期。</summary>
        public DateTime? EarliestDueHitDate { get; set; }
        /// <summary>交期命中行存在尚未反馈货好数量的风险，无权时为空。</summary>
        public bool? HasUnconfirmedReadyRisk { get; set; }
        /// <summary>允许读取维度的数量或证据核实原因，不泄露无权事实。</summary>
        public string ReviewReason { get; set; }
    }
}
