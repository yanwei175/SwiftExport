using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.SalesOrders;

/// <summary>销售订单头实体，保存客户订单的商务条件、交付要求和生命周期状态。</summary>
[Table("SalesOrderHeader")]
public partial class SalesOrderHeader : BaseEntity
{
    /// <summary>单据生命周期状态，用于判断当前记录允许执行的业务操作。</summary>
    public SalesOrderDocumentStatusEnum DocumentStatus { get; set; } = SalesOrderDocumentStatusEnum.Open;
    /// <summary>确认状态，用于判断当前记录允许执行的业务操作。</summary>
    public ConfirmationStatusEnum ConfirmationStatus { get; set; } = ConfirmationStatusEnum.待确认;

    /// <summary>该实体保存的销售订单号。</summary>
    public required string OrderNo { get; set; }
    /// <summary>该实体保存的商业发票号。</summary>
    public string? InvoiceNo { get; set; }

    /// <summary>关联的客户主数据唯一标识。</summary>
    public Guid? CustomerId { get; set; }
    /// <summary>该实体保存的客户业务编码。</summary>
    public string? CustomerCode { get; set; }
    /// <summary>该实体保存的客户采购单号。</summary>
    public string? CustomerPoNo { get; set; }

    /// <summary>该实体保存的运输方式。</summary>
    public string? ShippedBy { get; set; }

    /// <summary>该实体保存的交易币种。</summary>
    public string? Currency { get; set; }
    /// <summary>该实体保存的订单结算汇率。</summary>
    public decimal? ExchangeRate { get; set; }

    /// <summary>该实体保存的原币订单总金额。</summary>
    public decimal? TotalAmountOriginalCurrency { get; set; }
    /// <summary>该实体保存的本位币订单总金额。</summary>
    public decimal? TotalAmountLocalCurrency { get; set; }


    /// <summary>该实体保存的收货方名称。</summary>
    public string? ShipToName { get; set; }
    /// <summary>该实体保存的收货地址。</summary>
    public string? ShipToAddress { get; set; }
    /// <summary>该实体保存的收货方联系电话。</summary>
    public string? ShipToPhone { get; set; }

    /// <summary>如 &quot;FOB&quot;, &quot;CIF&quot;, &quot;DAP&quot; 等。</summary>
    public string? Incoterm { get; set; } // 如 "FOB", "CIF", "DAP" 等
    /// <summary>如 &quot;FOB Shanghai&quot;, &quot;CIF Los Angeles&quot;, &quot;DAP Berlin&quot; 等，表示具体的 Incoterm 位置。</summary>
    public string? IncotermLocation { get; set; } // 如 "FOB Shanghai", "CIF Los Angeles", "DAP Berlin" 等，表示具体的 Incoterm 位置
    /// <summary>该实体保存的贸易条款排序备注。</summary>
    public string? IncotermSortRemark { get; set; }

    /// <summary>预付款比例，如 30% 的预付款，DepositRate 就是 0.3。</summary>
    public decimal? DepositRate { get; set; } // 预付款比例，如 30% 的预付款，DepositRate 就是 0.3
    /// <summary>从订单日期到交货日期的天数。</summary>
    public int LeadTimeDays { get; set; } = 45;// 从订单日期到交货日期的天数
    /// <summary>如 &quot;预付款&quot;, &quot;信用证&quot;, &quot;月结&quot; 等。</summary>
    public string? PaymentMethod { get; set; }// 如 "预付款", "信用证", "月结" 等

    /// <summary>该实体保存的销售订单资料目录。</summary>
    public string? SoFolder { get; set; }
    /// <summary>该实体保存的业务备注。</summary>
    public string? Remark { get; set; }

    /// <summary>该实体保存的订单日期。</summary>
    public DateTime? OrderDate { get; set; } = DateTime.Now;
    /// <summary>订单交货日期（客户要求的交货日期）。</summary>
    public DateTime? DeliveryDate { get; set; } = DateTime.Now.AddDays(45);// 订单交货日期（客户要求的交货日期）
    /// <summary>销售订单回复日期（销售订单确认时的交货日期）。</summary>
    public DateTime? ReplyDate { get; set; }// 销售订单回复日期（销售订单确认时的交货日期）

    /// <summary>该实体保存的预计到达日期。</summary>
    public DateTime? ETA { get; set; }
    /// <summary>该实体保存的预计离港日期。</summary>
    public DateTime? ETD { get; set; }

    /// <summary>该实体保存的关闭原因。</summary>
    public string? CloseReason { get; set; }
    /// <summary>该实体保存的关闭时间。</summary>
    public DateTime? ClosedDate { get; set; }
    /// <summary>该实体保存的关闭操作人。</summary>
    public string? ClosedBy { get; set; }

}


