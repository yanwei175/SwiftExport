using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// 采购订单头实体（对应 dbo.PurchaseOrderHead）。
/// </summary>
[Table("PurchaseOrderHead")]
public class PurchaseOrderHead : BaseEntity
{
    public PurchaseOrderStatusEnum OrderStatus { get; set; } = PurchaseOrderStatusEnum.未确认;
    public ConfirmationStatusEnum ConfirmationStatus { get; set; } = ConfirmationStatusEnum.待确认;


    /// <summary>
    /// 关联字段：与明细保持一致，便于链路追踪。
    /// </summary>
    public required string OrderNo { get; set; }

    public string? BoNo { get; set; }
    public string? SalesOrderNo { get; set; }
    public string? CustomerPoNo { get; set; }
    public string? CustomerCode { get; set; }
    public string? Currency { get; set; }

    public required string SupplierCode { get; set; }
    public string? SupplierName { get; set; }
    public string? SoPerson { get; set; }
    public string? PoPerson { get; set; }


    public string? InletrHoseType { get; set; }
    public string? AeratorType { get; set; }
    public string? PalletType { get; set; }
    public string? BoxType { get; set; }
    public string? LabelType { get; set; }
    public string? IMType { get; set; }
    public string? CartonType { get; set; }
    public string? LogoType { get; set; }

    public string? ReviewedBy { get; set; }
    public string? PoFolder { get; set; }
    public string? ZheKouBeiZhu { get; set; }
    public string? Remark { get; set; }

    public bool? IsLocked { get; set; }
    public int? LineCount { get; set; }
    public decimal? TotalAmountLocalCurrency { get; set; }
    public decimal? TotalQty { get; set; }
    public decimal? TotalShippedQty { get; set; }
    public decimal? TotalUnshippedQty { get; set; }
    public decimal? TotalReadyQty { get; set; }

    public DateTime? OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public DateTime? ReplyDate { get; set; }

    public decimal? ZhiJieYouHuiJinE { get; set; }
    public decimal? ZheKou { get; set; }


}
