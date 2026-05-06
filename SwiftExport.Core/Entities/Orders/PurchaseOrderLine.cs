using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// 采购订单行实体（对应 dbo.PurchaseOrderLine）。
/// </summary>
[Table("PurchaseOrderLine")]
public class PurchaseOrderLine : BaseEntity
{
    public int? PurchaseOrderId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? CustomerSku { get; set; }
    public string? LinkSku { get; set; }
    public string? SupplierSku { get; set; }
    public string? DescriptionZh { get; set; }
    public string? FinishZh { get; set; }
    public string? FeaturesZh { get; set; }

    public decimal? UnitPrice { get; set; }
    public decimal? TaxPrice { get; set; }
    public decimal QTY { get; set; }
    public decimal? UnitAmount { get; set; }
    public decimal? TaxAmount { get; set; }

    public string? SubNote { get; set; }
    /// <summary>
    /// 供货策略（来自需求池 SupplyType，如：直接采购/生产/库存）。
    /// </summary>
    public string? SupplyType { get; set; }

    /// <summary>
    /// 物料分类（MaterialTypesEnum 字符串值）。
    /// </summary>
    public string? MaterialType { get; set; }

    /// <summary>
    /// 单件体积（CBM），转单时从产品档案带出，用于后续统计。
    /// </summary>
    public decimal? UnitCBM { get; set; }

    /// <summary>
    /// 单件重量（KG），转单时从产品档案带出，用于后续统计。
    /// </summary>
    public decimal? UnitWeight { get; set; }

    /// <summary>
    /// 供应商反馈完工数量。
    /// </summary>
    public decimal ReadyQty { get; set; }

    public string? DeliveryTo { get; set; }

    public DateTime? ChangeTime { get; set; }

    /// <summary>
    /// 来源需求池ID（回写需求池关键字段）。
    /// </summary>
    public long? DemandId { get; set; }

    /// <summary>
    /// 冗余字段,方便查询和统计分析，回写需求池时同步更新（SourceType + SourceNo 唯一索引）。
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// 冗余字段,方便查询和统计分析，回写需求池时同步更新（SourceType + SourceNo 唯一索引）。
    /// </summary>
    public string SourceNo { get; set; } = string.Empty;



    /// <summary>
    /// 财务实收数量。
    /// </summary>
    public decimal RecQty { get; set; }
    public DateTime? LastRecDate { get; set; }
    public string? FollowUpRemarks { get; set; }
    public DateTime? FollowUpTime { get; set; }
    public DateTime? FollowExpectedDate { get; set; }
}
