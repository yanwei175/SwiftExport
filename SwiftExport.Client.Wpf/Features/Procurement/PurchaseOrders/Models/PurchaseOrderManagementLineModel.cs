namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>采购订单管理页的新链从表只读投影；不承载编辑、来源贡献集合或进度历史。</summary>
public sealed partial class PurchaseOrderManagementLineModel
{
    /// <summary>采购行稳定身份，用于定位追溯，不以型号或行号代替。</summary>
    public Guid PurchaseOrderLineId { get; set; }
    /// <summary>采购行当前并发版本。</summary>
    public int Version { get; set; }
    public int LineNumber { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string MainImage { get; set; } = string.Empty;
    public string CustomerSku { get; set; } = string.Empty;
    public string LinkSku { get; set; } = string.Empty;
    public string SupplierSku { get; set; } = string.Empty;
    public string DescriptionZh { get; set; } = string.Empty;
    public string FinishZh { get; set; } = string.Empty;
    public string FeaturesZh { get; set; } = string.Empty;
    public string MaterialType { get; set; } = string.Empty;
    public string SupplyType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string UnitSnapshot { get; set; } = string.Empty;
    public string DeliverySupplierName { get; set; } = string.Empty;
    public DateTime? PlannedDeliveryDate { get; set; }
    public decimal? TaxPrice { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? UnitAmount { get; set; }
    /// <summary>可信有效贡献数量。</summary>
    public decimal? LinkedDemandQty { get; set; }
    /// <summary>尚未关联需求数量，不适用或无法核实时为空。</summary>
    public decimal? UnlinkedQty { get; set; }
    /// <summary>当前贡献不适用、未知和零数量使用不同展示。</summary>
    public string LinkedDemandQtyText => AssociationStatus == SwiftExport.Contracts.SysEnums.PurchaseDemandAssociationStatusEnum.NotApplicable
        ? "不适用" : LinkedDemandQty?.ToString("0.####") ?? "需核实";
    /// <summary>未关联数量不把异常或不适用当作零。</summary>
    public string UnlinkedQtyText => AssociationStatus == SwiftExport.Contracts.SysEnums.PurchaseDemandAssociationStatusEnum.NotApplicable
        ? "不适用" : UnlinkedQty?.ToString("0.####") ?? "需核实";
    /// <summary>有效关联的物料需求数。</summary>
    public int LinkedDemandCount { get; set; }
    /// <summary>服务端核验的关联状态。</summary>
    public SwiftExport.Contracts.SysEnums.PurchaseDemandAssociationStatusEnum AssociationStatus { get; set; }
    /// <summary>关联异常说明。</summary>
    public string AssociationReviewReason { get; set; } = string.Empty;
    /// <summary>仅解释需求关联核实边界；作废行明确当前关联不再适用。</summary>
    public string ReviewText => AssociationStatus == SwiftExport.Contracts.SysEnums.PurchaseDemandAssociationStatusEnum.NotApplicable
        ? "采购已作废，当前关联数量不适用；请查看历史贡献。" : AssociationReviewReason;
    public string SourceSummary { get; set; } = string.Empty;
    public string SubNote { get; set; } = string.Empty;
}
