using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Manager
{
    /// <summary>
    /// 销售订单管理页只读查询行。
    /// </summary>
    /// <remarks>来源表：SalesOrderHeader、Customer、SalesOrderLine。</remarks>
    public class SalesOrderManagerListItemDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public SalesOrderDocumentStatusEnum DocumentStatus { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        /// <summary>PackingList 有效交付净额接入后的履约状态；当前为空表示未知。</summary>
        public SalesOrderFulfillmentStatusEnum? FulfillmentStatus { get; set; }
        public string OrderNo { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public string CustomerPoNo { get; set; }
        public string InvoiceNo { get; set; }
        public string Currency { get; set; }
        public decimal ExchangeRate { get; set; }
        /// <summary>所有活动行单位相同且交付可核实时的订购数量，否则为空。</summary>
        public decimal? TotalQty { get; set; }
        /// <summary>可汇总数量的单位代码；多单位或无法核实时为空。</summary>
        public string UnitSnapshot { get; set; }
        /// <summary>可核实且同单位时的正式已交付数量，否则为空。</summary>
        public decimal? TotalShippedQty { get; set; }

        /// <summary>可核实且同单位时的商业未交数量，否则为空。</summary>
        public decimal? TotalUnshippedQty { get; set; }
        /// <summary>仍可能未完成的销售行中最早的有效客户交期。</summary>
        public DateTime? EarliestOpenDueDate { get; set; }
        /// <summary>仍可能未完成的销售行是否有缺失交期。</summary>
        public bool HasUndatedOpenLine { get; set; }
        /// <summary>可核实且逾期未交的行数。</summary>
        public int OverdueLineCount { get; set; }
        /// <summary>可核实且未来七日内临近交期的行数。</summary>
        public int DueSoonLineCount { get; set; }
        /// <summary>逾期命中行的最早有效交期。</summary>
        public DateTime? EarliestOverdueDate { get; set; }
        /// <summary>临期命中行的最早有效交期。</summary>
        public DateTime? EarliestDueSoonDate { get; set; }
        /// <summary>待确认订单交期已过的行数。</summary>
        public int PendingPastDueLineCount { get; set; }
        /// <summary>是否需要核实来源、过账历史、超发或空明细。</summary>
        public bool NeedsReview { get; set; }
        /// <summary>去重后的异常原因，供列表直接展示。</summary>
        public string ReviewReason { get; set; }
        public decimal TotalAmountOriginalCurrency { get; set; }
        public decimal TotalAmountLocalCurrency { get; set; }
        public DateTime? OrderDate { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public DateTime? ReplyDate { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string UpdatedBy { get; set; }
    }
}
