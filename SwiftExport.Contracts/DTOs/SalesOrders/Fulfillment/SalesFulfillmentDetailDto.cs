using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment
{
    /// <summary>销售订单履约只读详情，数量以各销售行单位为准。</summary>
    public sealed class SalesFulfillmentDetailDto
    {
        public Guid SalesOrderId { get; set; }
        public string OrderNo { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string InvoiceNo { get; set; } = string.Empty;
        public SalesOrderDocumentStatusEnum DocumentStatus { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public DateTime? OrderDate { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public List<SalesFulfillmentLineDto> Lines { get; set; } = new List<SalesFulfillmentLineDto>();
    }
}
