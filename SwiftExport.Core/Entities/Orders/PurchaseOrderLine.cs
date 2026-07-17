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
    public required Guid PurchaseOrderId { get; set; }
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>稳定的产品关系；遗留采购行允许为空。</summary>
    public Guid? ProductId { get; set; }

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

    /// <summary>由净需求计划交期形成的采购交期。</summary>
    public DateTime? PlannedDeliveryDate { get; set; }

    /// <summary>客供件送达目标的权威计划分配。</summary>
    public Guid? DeliveryTargetAllocationId { get; set; }

    /// <summary>客供件送达供应商 Uid。</summary>
    public Guid? DeliveryTargetSupplierId { get; set; }

    /// <summary>转单时的送达供应商代码快照。</summary>
    public string? DeliverySupplierCodeSnapshot { get; set; }

    /// <summary>转单时的送达供应商名称快照。</summary>
    public string? DeliverySupplierNameSnapshot { get; set; }
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


    /// <summary>
    /// 冗余字段,方便查询和统计分析，回写需求池时同步更新（SourceType + SourceNo 唯一索引）。
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// 冗余字段,方便查询和统计分析，回写需求池时同步更新（SourceType + SourceNo 唯一索引）。
    /// </summary>
    public string SourceNo { get; set; } = string.Empty;

    /// <summary>来源单据 Uid 快照。</summary>
    public Guid? SourceUid { get; set; }

    /// <summary>来源明细 Uid 快照。</summary>
    public Guid? SourceLineId { get; set; }

    /// <summary>来源行号快照。</summary>
    public int? SourceLineNumber { get; set; }
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
