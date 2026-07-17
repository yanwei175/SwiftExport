using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.DemandPlanning;

/// <summary>净需求链路创建的最小采购草稿头；物理表继续兼容 dbo.PurchaseOrderHead。</summary>
[Table("PurchaseOrderHead")]
public sealed class NetDemandPurchaseDraftHead : BaseEntity
{
    public int OrderStatus { get; set; }
    public ConfirmationStatusEnum ConfirmationStatus { get; set; } = ConfirmationStatusEnum.待确认;
    public PurchaseOrderLifecycleStatusEnum? LifecycleStatus { get; set; }
    public required string OrderNo { get; set; }
    public string? Currency { get; set; }
    public required string SupplierCode { get; set; }
    public string? SupplierName { get; set; }
    public Guid? SupplierId { get; set; }
    public string? DemandSourceType { get; set; }
    public Guid? DemandSourceUid { get; set; }
    public string? DemandSourceNo { get; set; }
    public Guid? ExecutionRequestId { get; set; }
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
}

/// <summary>净需求链路创建的最小采购草稿行；物理表继续兼容 dbo.PurchaseOrderLine。</summary>
[Table("PurchaseOrderLine")]
public sealed class NetDemandPurchaseDraftLine : BaseEntity
{
    public int ReadyStatus { get; set; }
    public int LineNumber { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public Guid? ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? CustomerSku { get; set; }
    public string? LinkSku { get; set; }
    public string? SupplierSku { get; set; }
    public string? DescriptionZh { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? TaxPrice { get; set; }
    public decimal QTY { get; set; }
    public decimal? UnitAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public DateTime? PlannedDeliveryDate { get; set; }
    public Guid? DeliveryTargetAllocationId { get; set; }
    public Guid? DeliveryTargetSupplierId { get; set; }
    public string? DeliverySupplierCodeSnapshot { get; set; }
    public string? DeliverySupplierNameSnapshot { get; set; }
    public string? SubNote { get; set; }
    public string? SupplyType { get; set; }
    public string? MaterialType { get; set; }
    public string? DeliveryTo { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceNo { get; set; } = string.Empty;
    public Guid? SourceUid { get; set; }
    public Guid? SourceLineId { get; set; }
    public int? SourceLineNumber { get; set; }
    public decimal ReadyQty { get; set; }
    public decimal RecQty { get; set; }
}
