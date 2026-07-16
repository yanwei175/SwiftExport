using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

[Table("SalesOrderHeader")]
public class SalesOrderHeader : BaseEntity
{
    public SalesOrderDocumentStatusEnum DocumentStatus { get; set; } = SalesOrderDocumentStatusEnum.Open;
    public ConfirmationStatusEnum ConfirmationStatus { get; set; } = ConfirmationStatusEnum.待确认;

    public required string OrderNo { get; set; }
    public string? InvoiceNo { get; set; }

    public Guid? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerPoNo { get; set; }

    public string? ShippedBy { get; set; }

    public string? Currency { get; set; }
    public decimal? ExchangeRate { get; set; }

    public decimal? TotalAmountOriginalCurrency { get; set; }
    public decimal? TotalAmountLocalCurrency { get; set; }


    public string? ShipToName { get; set; }
    public string? ShipToAddress { get; set; }
    public string? ShipToPhone { get; set; }

    public string? Incoterm { get; set; } // 如 "FOB", "CIF", "DAP" 等
    public string? IncotermLocation { get; set; } // 如 "FOB Shanghai", "CIF Los Angeles", "DAP Berlin" 等，表示具体的 Incoterm 位置
    public string? IncotermSortRemark { get; set; }

    public decimal? DepositRate { get; set; } // 预付款比例，如 30% 的预付款，DepositRate 就是 0.3
    public int LeadTimeDays { get; set; } = 45;// 从订单日期到交货日期的天数
    public string? PaymentMethod { get; set; }// 如 "预付款", "信用证", "月结" 等

    public string? SoFolder { get; set; }
    public string? Remark { get; set; }

    public DateTime? OrderDate { get; set; } = DateTime.Now;
    public DateTime? DeliveryDate { get; set; } = DateTime.Now.AddDays(45);// 订单交货日期（客户要求的交货日期）
    public DateTime? ReplyDate { get; set; }// 销售订单回复日期（销售订单确认时的交货日期）

    public DateTime? ETA { get; set; }
    public DateTime? ETD { get; set; }

    public string? CloseReason { get; set; }
    public DateTime? ClosedDate { get; set; }
    public string? ClosedBy { get; set; }

}


