using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Orders;

public class PurchaseOrder : BaseEntity
{
    public string PoNo { get; set; } = default!;
    public string? SoNo { get; set; }
    public string? CustomerPoNo { get; set; }

    public DateTime? PoDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public DateTime? ReplyDate { get; set; }

    public string? CustomerCode { get; set; }
    public string? SupplierCode { get; set; }
    public string? SupplierName { get; set; }

    public string? SoPerson { get; set; }
    public string? PoPerson { get; set; }
    public string? InletHoseType { get; set; }
    public string? AeratorType { get; set; }
    public string? PalletType { get; set; }
    public string? BoxType { get; set; }
    public string? LabelType { get; set; }
    public string? IMType { get; set; }
    public string? CartonType { get; set; }
    public string? LogoType { get; set; }
    public string? PoFolder { get; set; }
    public string? PoNote { get; set; }


    public decimal? DirectDiscountAmount { get; set; } // ZhiJieYouHuiJinE (直接优惠金额)
    public decimal? DiscountRate { get; set; }         // ZheKou (折扣)
    public string? DiscountRemarks { get; set; }       // ZheKouBeiZhu (折扣备注)


    public string? CreatedBy { get; set; }
    public string? ReviewedBy { get; set; }
    public string? ApprovedBy { get; set; }


    // 导航属性 (如果不用 EF Core，Dapper 中通常不需要 List<Item>，但为了模型完整性可以留着)
    // public List<PurchaseOrderItem> Items { get; set; } = new();

}
