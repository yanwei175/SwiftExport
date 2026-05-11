using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// 采购订单行实体（对应 dbo.PurchaseOrderLine）。
/// </summary>
[Table("PurchaseOrderLine")]
public class PurchaseOrderLine : BaseEntity
{
    public int ReadyStatus { get; set; }
    public int LineNumber { get; set; }
    public int? PurchaseOrderId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? CustomerSku { get; set; }
    public string? LinkSku { get; set; }
    public string? SupplierSku { get; set; }
    public string? DescriptionZh { get; set; }
    public string? FinishZh { get; set; }
    public string? FeaturesZh { get; set; }
    /// <summary>
    /// 单位价格
    /// </summary>
    public decimal? UnitPrice { get; set; }
    /// <summary>
    /// 含税价格
    /// </summary>
    public decimal? TaxPrice { get; set; }
    public decimal QTY { get; set; }
    /// <summary>
    /// 单价小计（UnitPrice * QTY），回写需求池时同步更新。
    /// </summary>
    public decimal? UnitAmount { get; set; }
    /// <summary>
    /// 含税价小计(TaxPrice * QTY)，回写需求池时同步更新。
    /// </summary>
    public decimal? TaxAmount { get; set; }
    /// <summary>
    /// 行备注
    /// </summary>
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
    /// 送货至（DeliveryTo），转单时从需求池带出，回写需求池时同步更新，方便统计分析不同送货地点的订单情况。
    /// </summary>
    public string? DeliveryTo { get; set; }

    public DateTime? ChangeTime { get; set; }


    /// <summary>
    /// 冗余字段,方便查询和统计分析，回写需求池时同步更新（SourceType + SourceNo 唯一索引）。
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// 冗余字段,方便查询和统计分析，回写需求池时同步更新（SourceType + SourceNo 唯一索引）。
    /// </summary>
    public string SourceNo { get; set; } = string.Empty;
    ///// <summary>
    ///// 毛需求打散成了净需求,再聚合成了采购单,这个Id根本就对应不上了,所以这个字段没什么意义,先注释掉,后续如果有需要再说
    ///// </summary>
    //public int? SourceLineId { get; set; }

    #region 外部更新字段
    /// <summary>
    /// 供应商反馈完工数量。
    /// </summary>
    public decimal ReadyQty { get; set; }
    /// <summary>
    /// 财务实收数量。
    /// </summary>
    public decimal RecQty { get; set; }
    /// <summary>
    /// 最近一次收货时间
    /// </summary>
    public DateTime? LastRecDate { get; set; }
    /// <summary>
    /// 订单跟进备注
    /// </summary>
    public string? FollowUpRemarks { get; set; }
    /// <summary>
    /// 订单跟进时间
    /// </summary>
    public DateTime? FollowUpTime { get; set; }
    /// <summary>
    /// 获取订单的预计交期，优先级：供应商反馈的完工日期 > 订单跟进时间 + 7天（默认生产周期） > 订单跟进时间 + 15天（最长生产周期）。该字段用于统计分析订单交期情况，回写需求池时同步更新。
    /// </summary>
    public DateTime? FollowExpectedDate { get; set; }
    #endregion
}
