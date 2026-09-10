using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>采购从物料需求取数时的页面候选模型，独立持有选择状态和采购展示字段。</summary>
public sealed partial class PurchaseCandidateItemModel : ObservableObject
{
    /// <summary>物料需求 Uid。</summary>
    public Guid Uid { get; init; }
    /// <summary>物料需求并发版本。</summary>
    public int Version { get; init; }
    /// <summary>来源业务类型。</summary>
    public string SourceType { get; init; } = string.Empty;
    /// <summary>来源业务 Uid。</summary>
    public Guid SourceUid { get; init; }
    /// <summary>来源业务单号。</summary>
    public string SourceNo { get; init; } = string.Empty;
    /// <summary>来源业务行号。</summary>
    public int? SourceLineNumber { get; init; }
    /// <summary>物料 SKU 快照。</summary>
    public string Sku { get; init; } = string.Empty;
    /// <summary>是否为送往父件实际执行供应商的客供件。</summary>
    public bool IsCustomerSupplied { get; init; }
    /// <summary>产品关联 SKU。</summary>
    public string LinkSku { get; init; } = string.Empty;
    /// <summary>物料描述。</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>产品图片路径。</summary>
    public string ImagePath { get; init; } = string.Empty;
    /// <summary>推荐采购供应商 Uid。</summary>
    public Guid? SuggestedPurchaseSupplierId { get; init; }
    /// <summary>推荐采购供应商编码。</summary>
    public string SuggestedPurchaseSupplierCode { get; init; } = string.Empty;
    /// <summary>推荐采购供应商名称。</summary>
    public string SuggestedPurchaseSupplierName { get; init; } = string.Empty;
    /// <summary>建议送达供应商 Uid。</summary>
    public Guid? SuggestedDeliverySupplierId { get; init; }
    /// <summary>建议送达供应商编码。</summary>
    public string SuggestedDeliverySupplierCode { get; init; } = string.Empty;
    /// <summary>建议送达供应商名称。</summary>
    public string SuggestedDeliverySupplierName { get; init; } = string.Empty;
    /// <summary>建议送达方无法确定时的事实提示。</summary>
    public string SuggestedDeliverySupplierHint { get; init; } = string.Empty;
    /// <summary>客供件父件活动采购单中的实际采购供应商事实。</summary>
    public List<PurchaseDraftActualSupplierFactDto> ParentActualPurchaseSuppliers { get; init; } = [];
    /// <summary>来源需求交期。</summary>
    public DateTime? DemandRequiredDate { get; init; }
    /// <summary>当前尚未被有效采购贡献承接的数量。</summary>
    public decimal RemainingQty { get; init; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    /// <summary>从只读候选契约建立采购页面模型。</summary>
    public static PurchaseCandidateItemModel FromDto(PurchaseDraftCandidateDto dto)
        => dto.Adapt<PurchaseCandidateItemModel>();
}
