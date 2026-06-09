using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    public class SalesOrderHeaderDto : BaseDto
    {
        public Guid CustomerId { get; set; }
        public SalesOrderStatusEnum OrderStatus { get; set; }
        public ConfirmationStatusEnum ConfirmationStatus { get; set; }
        public ApprovalStatusEnum ApprovalStatus { get; set; }

        public string OrderNo { get; set; }
        public string InvoiceNo { get; set; }

        public string CustomerCode { get; set; }
        public string CustomerPoNo { get; set; }
        public string ShippedBy { get; set; }
        public string Currency { get; set; }// 货币
        public decimal ExchangeRate { get; set; }// 汇率
        public decimal? TotalAmountOriginalCurrency { get; set; }
        public decimal? TotalAmountLocalCurrency { get; set; }
        public string ShipToName { get; set; }
        public string ShipToAddress { get; set; }
        public string ShipToPhone { get; set; }
        public string Incoterm { get; set; }// 贸易术语
        public string IncotermSortRemark { get; set; }// 贸易术语备注
        public string IncotermLocation { get; set; }// 贸易术语地点
        public decimal DepositRate { get; set; }// 定金比例
        public int LeadTimeDays { get; set; }// 交货周期（天数）
        public string PaymentMethod { get; set; }// 付款方式
        public string SoFolder { get; set; }
        public string Remark { get; set; }

        public DateTime? OrderDate { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public DateTime? ReplyDate { get; set; }
        public DateTime? ETA { get; set; }
        public DateTime? ETD { get; set; }

    }

}
