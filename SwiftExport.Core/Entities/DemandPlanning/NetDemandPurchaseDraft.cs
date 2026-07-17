using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.DemandPlanning;

/// <summary>净需求链路创建的最小采购草稿头；物理表继续兼容 dbo.PurchaseOrderHead。</summary>
[Table("PurchaseOrderHead")]
public sealed class NetDemandPurchaseDraftHead : BaseEntity
{
    /// <summary>旧表兼容状态，新净需求链路固定写 0，禁止据此判断业务状态。</summary>
    public int OrderStatus { get; set; }
    /// <summary>采购草稿确认状态；新建时为待确认。</summary>
    public ConfirmationStatusEnum ConfirmationStatus { get; set; } = ConfirmationStatusEnum.待确认;
    /// <summary>采购草稿生命周期；活动、关闭或作废。</summary>
    public PurchaseOrderLifecycleStatusEnum? LifecycleStatus { get; set; }
    /// <summary>采购单号。</summary>
    public required string OrderNo { get; set; }
    /// <summary>币种代码；当前净需求采购固定人民币。</summary>
    public string? Currency { get; set; }
    /// <summary>采购供应商编码快照。</summary>
    public required string SupplierCode { get; set; }
    /// <summary>采购供应商名称快照。</summary>
    public string? SupplierName { get; set; }
    /// <summary>采购供应商 Uid，是新链路的供应商权威关联。</summary>
    public Guid? SupplierId { get; set; }
    /// <summary>形成采购草稿的需求来源类型。</summary>
    public string? DemandSourceType { get; set; }
    /// <summary>形成采购草稿的来源单据 Uid。</summary>
    public Guid? DemandSourceUid { get; set; }
    /// <summary>形成采购草稿的来源单号快照。</summary>
    public string? DemandSourceNo { get; set; }
    /// <summary>采购草稿创建请求的幂等 Uid。</summary>
    public Guid? ExecutionRequestId { get; set; }
    /// <summary>采购草稿备注。</summary>
    public string? Remark { get; set; }
    /// <summary>旧表兼容锁定标记；新链路不以此替代生命周期判断。</summary>
    public bool? IsLocked { get; set; }
    /// <summary>采购行数汇总。</summary>
    public int? LineCount { get; set; }
    /// <summary>本币金额汇总。</summary>
    public decimal? TotalAmountLocalCurrency { get; set; }
    /// <summary>采购总数量汇总。</summary>
    public decimal? TotalQty { get; set; }
    /// <summary>已发运数量汇总，供未来跟单链路使用。</summary>
    public decimal? TotalShippedQty { get; set; }
    /// <summary>未发运数量汇总，供未来跟单链路使用。</summary>
    public decimal? TotalUnshippedQty { get; set; }
    /// <summary>供应商已备好数量汇总，供未来跟单链路使用。</summary>
    public decimal? TotalReadyQty { get; set; }
    /// <summary>采购单日期。</summary>
    public DateTime? OrderDate { get; set; }
    /// <summary>采购单预计交付日期。</summary>
    public DateTime? ExpectedDate { get; set; }
}

/// <summary>净需求链路创建的最小采购草稿行；物理表继续兼容 dbo.PurchaseOrderLine。</summary>
[Table("PurchaseOrderLine")]
public sealed class NetDemandPurchaseDraftLine : BaseEntity
{
    /// <summary>旧表兼容备货状态；净需求链路当前固定写默认值。</summary>
    public int ReadyStatus { get; set; }
    /// <summary>采购单内稳定行号。</summary>
    public int LineNumber { get; set; }
    /// <summary>所属采购单头 Uid。</summary>
    public Guid PurchaseOrderId { get; set; }
    /// <summary>采购单号快照。</summary>
    public string OrderNo { get; set; } = string.Empty;
    /// <summary>关联产品 Uid；非标件可以为空。</summary>
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
    /// <summary>未税单价；采购资料未完成时可以为空。</summary>
    public decimal? UnitPrice { get; set; }
    /// <summary>含税单价；采购资料未完成时可以为空。</summary>
    public decimal? TaxPrice { get; set; }
    /// <summary>本采购行数量，物理列沿用历史名称 QTY。</summary>
    public decimal QTY { get; set; }
    /// <summary>未税金额。</summary>
    public decimal? UnitAmount { get; set; }
    /// <summary>含税金额。</summary>
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
    /// <summary>旧表兼容的可读送达说明；权威目标仍是 DeliveryTargetSupplierId。</summary>
    public string? DeliveryTo { get; set; }
    /// <summary>需求来源类型快照。</summary>
    public string SourceType { get; set; } = string.Empty;
    /// <summary>需求来源单号快照。</summary>
    public string SourceNo { get; set; } = string.Empty;
    /// <summary>需求来源单据 Uid。</summary>
    public Guid? SourceUid { get; set; }
    /// <summary>需求来源明细行 Uid。</summary>
    public Guid? SourceLineId { get; set; }
    /// <summary>需求来源明细行号快照。</summary>
    public int? SourceLineNumber { get; set; }
    /// <summary>供应商已备好数量，当前仅兼容保存，未来由跟单链路维护。</summary>
    public decimal ReadyQty { get; set; }
    /// <summary>已收货数量，当前仅兼容保存，未来由入库链路维护。</summary>
    public decimal RecQty { get; set; }
}
