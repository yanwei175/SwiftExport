using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Procurement;

/// <summary>
/// 采购订单行实体，保存采购数量、商业条件、产品快照和送达目标。
/// 已备好、已入库和已发货数量分别来自跟单、入库和出库事实，不在本实体缓存。
/// 对应物理表 dbo.PurchaseOrderLine。
/// </summary>
[Table("PurchaseOrderLine")]
public sealed class PurchaseOrderLine : BaseEntity
{
    /// <summary>不由需求贡献承担的数量；历史未知保持空值，独立采购等于采购数量。</summary>
    public decimal? UnlinkedQty { get; set; }
    /// <summary>明确收货仓库，不依赖需求分配，不等同于 BOM 客供件消耗仓库。</summary>
    public Guid? DeliveryWarehouseSiteId { get; set; }
    /// <summary>收货仓库代码历史快照。</summary>
    public string? DeliveryWarehouseCodeSnapshot { get; set; }
    /// <summary>收货仓库名称历史快照。</summary>
    public string? DeliveryWarehouseNameSnapshot { get; set; }
    /// <summary>所属采购订单头 Uid。</summary>
    public Guid PurchaseOrderId { get; set; }

    /// <summary>采购单内稳定行号。</summary>
    public int LineNumber { get; set; }

    /// <summary>采购单号快照。</summary>
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>关联产品 Uid；非标件允许为空。</summary>
    public Guid? ProductId { get; set; }

    /// <summary>采购 SKU 快照。</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>客户型号快照。</summary>
    public string? CustomerSku { get; set; }

    /// <summary>关联 SKU 快照。</summary>
    public string? LinkSku { get; set; }

    /// <summary>供应商产品型号快照。</summary>
    public string? SupplierSku { get; set; }

    /// <summary>采购品中文描述快照。</summary>
    public string? DescriptionZh { get; set; }

    /// <summary>表面处理或颜色快照。</summary>
    public string? FinishZh { get; set; }

    /// <summary>产品配置快照。</summary>
    public string? FeaturesZh { get; set; }

    /// <summary>采购单位快照。</summary>
    public string? UnitSnapshot { get; set; }

    /// <summary>采购数量；物理列继续使用历史名称 QTY。</summary>
    [Column("QTY")]
    public decimal Quantity { get; set; }

    /// <summary>未税单价；采购资料未完成时允许为空，明确零价有效。</summary>
    public decimal? UnitPrice { get; set; }

    /// <summary>含税单价；采购资料未完成时允许为空，明确零价有效。</summary>
    public decimal? TaxPrice { get; set; }

    /// <summary>未税金额快照，由数量和未税单价计算。</summary>
    public decimal? UnitAmount { get; set; }

    /// <summary>含税金额快照，由数量和含税单价计算。</summary>
    public decimal? TaxAmount { get; set; }

    /// <summary>采购计划交付日期。</summary>
    public DateTime? PlannedDeliveryDate { get; set; }

    /// <summary>客供件送达目标的 Allocation Uid。</summary>
    public Guid? DeliveryTargetAllocationId { get; set; }

    /// <summary>客供件送达目标供应商 Uid。</summary>
    public Guid? DeliveryTargetSupplierId { get; set; }

    /// <summary>送达目标供应商编码快照。</summary>
    public string? DeliverySupplierCodeSnapshot { get; set; }

    /// <summary>送达目标供应商名称快照。</summary>
    public string? DeliverySupplierNameSnapshot { get; set; }

    /// <summary>采购行附加说明。</summary>
    public string? SubNote { get; set; }

    /// <summary>供货方式快照。</summary>
    public string? SupplyType { get; set; }

    /// <summary>物料类型快照。</summary>
    public string? MaterialType { get; set; }

    /// <summary>需求来源类型快照。</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>需求来源单号快照。</summary>
    public string SourceNo { get; set; } = string.Empty;

    /// <summary>需求来源单据 Uid。</summary>
    public Guid? SourceUid { get; set; }

    /// <summary>单一来源时保存来源明细 Uid；聚合多来源时为空，权威关系见 DemandExecutionLink。</summary>
    public Guid? SourceLineId { get; set; }

    /// <summary>单一来源时保存来源行号快照。</summary>
    public int? SourceLineNumber { get; set; }
}
