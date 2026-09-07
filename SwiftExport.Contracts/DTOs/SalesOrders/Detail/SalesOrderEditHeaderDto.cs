using SwiftExport.Contracts.SysEnums;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders.Detail
{
    /// <summary>销售订单详情中可编辑的订单头商业快照。</summary>
    public class SalesOrderEditHeaderDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public SalesOrderDocumentStatusEnum DocumentStatus { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string OrderNo { get; set; }
        public string InvoiceNo { get; set; }
        public string CustomerPoNo { get; set; }
        public string ShippedBy { get; set; }
        public string Currency { get; set; }
        public decimal ExchangeRate { get; set; }
        public string ShipToName { get; set; }
        public string ShipToAddress { get; set; }
        public string ShipToPhone { get; set; }
        public string Incoterm { get; set; }
        public string IncotermSortRemark { get; set; }
        public string IncotermLocation { get; set; }
        public decimal DepositRate { get; set; }
        public int LeadTimeDays { get; set; }
        public string PaymentMethod { get; set; }
        public string SoFolder { get; set; }
        public string Remark { get; set; }
        public DateTime? OrderDate { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public DateTime? ReplyDate { get; set; }
        public DateTime? Eta { get; set; }
        public DateTime? Etd { get; set; }
    }
}
