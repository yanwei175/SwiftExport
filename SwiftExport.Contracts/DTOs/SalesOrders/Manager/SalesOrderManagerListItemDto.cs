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
        public decimal TotalQty { get; set; }
        /// <summary>PackingList 有效交付净额接入后的已发数量；当前为空表示未知。</summary>
        public decimal? TotalShippedQty { get; set; }

        /// <summary>PackingList 有效交付净额接入后的未发数量；当前为空表示未知。</summary>
        public decimal? TotalUnshippedQty { get; set; }
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
