using SwiftExport.Contracts.DTOs.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    public class SalesOrderHeaderDto : BaseDto
    {
        public string OrderStatus { get; set; }
        public string OrderNo { get; set; }
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerPoNo { get; set; }
        public string ShippedBy { get; set; }
        public string Currency { get; set; }
        public decimal ExchangeRate { get; set; }
        public decimal? TotalAmountOriginalCurrency { get; set; }
        public decimal? TotalAmountLocalCurrency { get; set; }
        public decimal? TotalQty { get; set; }
        public int? LineCount { get; set; }
        public decimal? TotalShippedQty { get; set; }
        public decimal? TotalUnshippedQty { get; set; }
        public decimal? TotalPartiallyShippedQty { get; set; }
        public string InvoiceNo { get; set; }
        public string ShipToName { get; set; }
        public string ShipToAddress { get; set; }
        public string ShipToPhone { get; set; }
        public string Incoterm { get; set; }
        public string IncotermSortRemark { get; set; }
        public string IncotermLocation { get; set; }
        public decimal DepositRate { get; set; }
        public int LeadTimeDays { get; set; }
        public string PaymentMethod { get; set; }
        public string VersionNo { get; set; }
        public string ApprovalStatus { get; set; }
        public string SoFolder { get; set; }
        public string Remark { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsLocked { get; set; }

        public DateTime OrderDate { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public DateTime? ReplyDate { get; set; }
        public DateTime? ETA { get; set; }
        public DateTime? ETD { get; set; }

        public DateTime ChangeTime { get; set; }
    }

}
