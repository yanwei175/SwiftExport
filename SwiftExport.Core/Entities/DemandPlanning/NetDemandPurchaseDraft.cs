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
    /// <summary>诗亦霖内部采购单号快照。</summary>
    public string? BoNo { get; set; }
    /// <summary>遗留销售来源单号快照；新链路优先读取 DemandSourceNo。</summary>
    public string? SalesOrderNo { get; set; }
    /// <summary>客户采购单号快照。</summary>
    public string? CustomerPoNo { get; set; }
    /// <summary>客户代码快照。</summary>
    public string? CustomerCode { get; set; }
    /// <summary>币种代码；当前净需求采购固定人民币。</summary>
    public string? Currency { get; set; }
    /// <summary>采购供应商编码快照。</summary>
    public required string SupplierCode { get; set; }
    /// <summary>采购供应商名称快照。</summary>
    public string? SupplierName { get; set; }
    /// <summary>业务员名称快照。</summary>
    public string? SoPerson { get; set; }
    /// <summary>采购或跟单员名称快照。</summary>
    public string? PoPerson { get; set; }
    /// <summary>进水管要求。</summary>
    public string? InletrHoseType { get; set; }
    /// <summary>起泡器要求。</summary>
    public string? AeratorType { get; set; }
    /// <summary>托盘要求。</summary>
    public string? PalletType { get; set; }
    /// <summary>内盒要求。</summary>
    public string? BoxType { get; set; }
    /// <summary>标贴要求。</summary>
    public string? LabelType { get; set; }
    /// <summary>说明书要求。</summary>
    public string? IMType { get; set; }
    /// <summary>外箱要求。</summary>
    public string? CartonType { get; set; }
    /// <summary>Logo 或标识要求。</summary>
    public string? LogoType { get; set; }
    /// <summary>复核人快照。</summary>
    public string? ReviewedBy { get; set; }
    /// <summary>采购文件夹路径或编号。</summary>
    public string? PoFolder { get; set; }
    /// <summary>折扣及特殊商务备注。</summary>
    public string? ZheKouBeiZhu { get; set; }
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
    /// <summary>供应商回复交期。</summary>
    public DateTime? ReplyDate { get; set; }
    /// <summary>最近一次确认日期；取消确认时清空，完整历史由业务操作日志保留。</summary>
    public DateTime? ConfirmedDate { get; set; }
    /// <summary>最近一次确认人名称；取消确认时清空。</summary>
    public string? ConfirmedBy { get; set; }
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
    /// <summary>表面处理或颜色快照。</summary>
    public string? FinishZh { get; set; }
    /// <summary>产品配置快照。</summary>
    public string? FeaturesZh { get; set; }
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
    /// <summary>最近一次遗留收货日期，仅用于下游安全检查和兼容展示。</summary>
    public DateTime? LastRecDate { get; set; }
}
