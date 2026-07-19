using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Workspace
{
    /// <summary>
    /// 销售订单管理页只读查询行。
    /// </summary>
    /// <remarks>来源表：SalesOrderHeader、Customer、SalesOrderLine。</remarks>
    public class SalesOrderListItemDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public SalesOrderDocumentStatusEnum DocumentStatus { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public SalesOrderFulfillmentStatusEnum FulfillmentStatus { get; set; }
        public string OrderNo { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public string CustomerPoNo { get; set; }
        public string InvoiceNo { get; set; }
        public string Currency { get; set; }
        public decimal ExchangeRate { get; set; }
        public decimal TotalQty { get; set; }
        public decimal TotalShippedQty { get; set; }
        public decimal TotalUnshippedQty { get; set; }
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
