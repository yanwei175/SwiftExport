using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

/// <summary>表示物料需求工作台的一条物料事实，并仅跟踪推荐采购供应商的客户端编辑状态。</summary>
public sealed partial class MaterialDemandManagerRowModel : ObservableObject
{
    private Guid? _originalSuggestedPurchaseSupplierId;
    private string _originalSuggestedPurchaseSupplierCode = string.Empty;
    private string _originalSuggestedPurchaseSupplierName = string.Empty;

    /// <summary>物料需求 Uid。</summary>
    public Guid Uid { get; init; }
    /// <summary>物料需求并发版本。</summary>
    public int Version { get; set; }
    /// <summary>物料需求生命周期。</summary>
    public NetDemandLifecycleStatusEnum LifecycleStatus { get; init; }
    /// <summary>所属来源需求 Uid。</summary>
    public Guid GrossDemandId { get; init; }
    /// <summary>来源需求拆解修订号。</summary>
    public int DecompositionRevision { get; init; }
    /// <summary>拆解修订内行号。</summary>
    public int LineNumber { get; init; }
    /// <summary>来源业务类型。</summary>
    public string SourceType { get; init; } = string.Empty;
    /// <summary>来源业务单据 Uid。</summary>
    public Guid SourceUid { get; init; }
    /// <summary>来源业务单号。</summary>
    public string SourceNo { get; init; } = string.Empty;
    /// <summary>来源业务行号。</summary>
    public int? SourceLineNumber { get; init; }
    /// <summary>来源业务行 Uid。</summary>
    public Guid SourceLineId { get; init; }
    /// <summary>拆解树根物料 Uid。</summary>
    public Guid RootNetDemandId { get; init; }
    /// <summary>拆解树根 SKU。</summary>
    public string RootSku { get; init; } = string.Empty;
    /// <summary>是否为客供件。</summary>
    public bool IsCustomerSupplied { get; init; }
    /// <summary>客供件对应的送达目标物料需求 Uid。</summary>
    public Guid? DeliveryTargetNetDemandId { get; init; }
    /// <summary>关联产品 Uid。</summary>
    public Guid? ProductId { get; init; }
    /// <summary>是否为非标物料。</summary>
    public bool IsNonStandard { get; init; }
    /// <summary>物料 SKU。</summary>
    public string Sku { get; init; } = string.Empty;
    /// <summary>产品关联 SKU。</summary>
    public string LinkSku { get; init; } = string.Empty;
    /// <summary>客户型号。</summary>
    public string CustomerSku { get; init; } = string.Empty;
    /// <summary>物料描述。</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>颜色。</summary>
    public string Color { get; init; } = string.Empty;
    /// <summary>规格。</summary>
    public string Specification { get; init; } = string.Empty;
    /// <summary>图片路径。</summary>
    public string ImagePath { get; init; } = string.Empty;
    /// <summary>物料类型。</summary>
    public string MaterialType { get; init; } = string.Empty;
    /// <summary>计量单位。</summary>
    public string Unit { get; init; } = string.Empty;
    /// <summary>拆解时供货方式快照。</summary>
    public string SupplyType { get; init; } = string.Empty;
    /// <summary>可编辑的推荐采购供应商 Uid；仅为建议，不会修改既有采购单。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSuggestionDirty))]
    [NotifyPropertyChangedFor(nameof(SuggestedPurchaseSupplierDisplay))]
    public partial Guid? SuggestedPurchaseSupplierId { get; set; }

    /// <summary>可编辑推荐采购供应商的当前编码。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedPurchaseSupplierDisplay))]
    public partial string SuggestedPurchaseSupplierCode { get; set; } = string.Empty;

    /// <summary>可编辑推荐采购供应商的当前名称。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedPurchaseSupplierDisplay))]
    public partial string SuggestedPurchaseSupplierName { get; set; } = string.Empty;

    /// <summary>编辑器使用的当前合法供应商对象；由已保存字段初始化，不依赖远程候选集合包含原项。</summary>
    [ObservableProperty]
    public partial ProcurementSupplierCandidateDto? SuggestedPurchaseSupplierSelection { get; private set; }

    /// <summary>客供件沿父件建议派生的只读送达供应商 Uid。</summary>
    public Guid? SuggestedDeliverySupplierId { get; init; }
    /// <summary>建议送达供应商编码；不代表采购域最终送达方。</summary>
    public string SuggestedDeliverySupplierCode { get; init; } = string.Empty;
    /// <summary>建议送达供应商名称；不代表采购域最终送达方。</summary>
    public string SuggestedDeliverySupplierName { get; init; } = string.Empty;
    /// <summary>无法派生客供件建议送达供应商时的轻量提示。</summary>
    public string SuggestedDeliverySupplierHint { get; init; } = string.Empty;
    /// <summary>原始需求数量。</summary>
    public decimal GrossQty { get; init; }
    /// <summary>当前净需求数量。</summary>
    public decimal NetQty { get; init; }
    /// <summary>来源需求交期。</summary>
    public DateTime? DemandRequiredDate { get; init; }
    /// <summary>有效采购 Link 的净关联数量。</summary>
    public decimal? AssociatedPurchaseQty { get; init; }
    /// <summary>实际采购供应商摘要，区别于本行推荐供应商。</summary>
    public string ActualPurchaseSuppliers { get; init; } = string.Empty;
    /// <summary>有效采购承接进度。</summary>
    public string ProcurementStatus { get; init; } = string.Empty;
    /// <summary>服务端确定的交期提示。</summary>
    public string DueHint { get; init; } = string.Empty;
    /// <summary>无法证明来源或采购数量时的原因。</summary>
    public string ReviewReason { get; init; } = string.Empty;
    /// <summary>历史物料只允许追踪，不允许编辑建议。</summary>
    public bool IsSuggestionEditable => LifecycleStatus == NetDemandLifecycleStatusEnum.Active;
    /// <summary>有效采购 Link 对应的采购单号。</summary>
    public string PurchaseOrderNos { get; init; } = string.Empty;

    /// <summary>当前尚未被有效采购 Link 承接的数量。</summary>
    public decimal? RemainingQty => AssociatedPurchaseQty.HasValue
        ? Math.Max(0m, NetQty - AssociatedPurchaseQty.Value) : null;

    /// <summary>推荐采购供应商是否有尚未保存的本地修改。</summary>
    public bool IsSuggestionDirty => SuggestedPurchaseSupplierId != _originalSuggestedPurchaseSupplierId;

    /// <summary>推荐采购供应商的显示文本。</summary>
    public string SuggestedPurchaseSupplierDisplay => string.IsNullOrWhiteSpace(SuggestedPurchaseSupplierCode)
        ? "未设置"
        : $"{SuggestedPurchaseSupplierName}".TrimEnd(' ', '·');

    /// <summary>建议送达供应商的只读显示文本。</summary>
    public string SuggestedDeliverySupplierDisplay => !string.IsNullOrWhiteSpace(SuggestedDeliverySupplierCode)
        ? $"{SuggestedDeliverySupplierName}".TrimEnd(' ', '·')
        : IsCustomerSupplied ? SuggestedDeliverySupplierHint : string.Empty;

    /// <summary>物料生命周期显示文本。</summary>
    public string LifecycleStatusText => LifecycleStatus switch
    {
        NetDemandLifecycleStatusEnum.Active => "活动",
        NetDemandLifecycleStatusEnum.RolledBack => "已回退",
        NetDemandLifecycleStatusEnum.Voided => "已作废",
        _ => LifecycleStatus.ToString(),
    };

    /// <summary>来源类型显示文本。</summary>
    public string SourceTypeText => SourceType switch
    {
        "SO" => "销售订单",
        "MD" => "手工需求",
        "CR" => "投诉补货",
        _ => SourceType,
    };

    /// <summary>父件或客供件显示文本。</summary>
    public string DemandRoleText => IsCustomerSupplied ? "客供件" : "父件";

    /// <summary>带层级缩进的 SKU。</summary>
    public string DisplaySku => IsCustomerSupplied ? $" ↳ {Sku}" : Sku;

    /// <summary>客户端当前是否勾选该行。</summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    /// <summary>把专用只读 DTO 转换为包含客户端选择状态的行模型。</summary>
    public static MaterialDemandManagerRowModel FromDto(MaterialDemandManagerItemDto dto)
    {
        var model = dto.Adapt<MaterialDemandManagerRowModel>();
        model._originalSuggestedPurchaseSupplierId = model.SuggestedPurchaseSupplierId;
        model._originalSuggestedPurchaseSupplierCode = model.SuggestedPurchaseSupplierCode;
        model._originalSuggestedPurchaseSupplierName = model.SuggestedPurchaseSupplierName;
        model.SuggestedPurchaseSupplierSelection = CreateSuggestedSupplierSelection(
            model.SuggestedPurchaseSupplierId,
            model.SuggestedPurchaseSupplierCode,
            model.SuggestedPurchaseSupplierName);
        return model;
    }

    /// <summary>只在 Lookup 明确提交候选后接纳选择，普通输入、失焦和编辑器卸载不会调用本命令。</summary>
    [RelayCommand]
    private void CommitSuggestedSupplierSelection(ProcurementSupplierCandidateDto? supplier)
    {
        if (IsSuggestionEditable && supplier is not null)
        {
            ApplySuggestedSupplier(supplier);
        }
    }

    /// <summary>只响应 Lookup 清除按钮的显式清空操作。</summary>
    [RelayCommand]
    private void ClearSuggestedSupplierSelection()
    {
        if (IsSuggestionEditable) ClearSuggestedSupplier();
    }

    /// <summary>接纳 Lookup 中明确选择的供应商并保留本地脏状态。</summary>
    public void ApplySuggestedSupplier(ProcurementSupplierCandidateDto supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        SuggestedPurchaseSupplierId = supplier.Uid;
        SuggestedPurchaseSupplierCode = supplier.SupplierCode;
        SuggestedPurchaseSupplierName = supplier.SupplierName;
        SuggestedPurchaseSupplierSelection = supplier;
    }

    /// <summary>显式清空推荐采购供应商；查询刷新前不会从产品默认值重新回填。</summary>
    public void ClearSuggestedSupplier()
    {
        SuggestedPurchaseSupplierId = null;
        SuggestedPurchaseSupplierCode = string.Empty;
        SuggestedPurchaseSupplierName = string.Empty;
        SuggestedPurchaseSupplierSelection = null;
    }

    /// <summary>接纳服务端成功结果并推进并发版本及原值快照。</summary>
    public void AcceptSuggestedSupplierUpdate(UpdateSuggestedPurchaseSupplierResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Version = result.Version;
        SuggestedPurchaseSupplierId = result.SuggestedPurchaseSupplierId;
        SuggestedPurchaseSupplierCode = result.SuggestedPurchaseSupplierCode ?? string.Empty;
        SuggestedPurchaseSupplierName = result.SuggestedPurchaseSupplierName ?? string.Empty;
        SuggestedPurchaseSupplierSelection = CreateSuggestedSupplierSelection(
            SuggestedPurchaseSupplierId,
            SuggestedPurchaseSupplierCode,
            SuggestedPurchaseSupplierName);
        _originalSuggestedPurchaseSupplierId = SuggestedPurchaseSupplierId;
        _originalSuggestedPurchaseSupplierCode = SuggestedPurchaseSupplierCode;
        _originalSuggestedPurchaseSupplierName = SuggestedPurchaseSupplierName;
        OnPropertyChanged(nameof(IsSuggestionDirty));
    }

    /// <summary>撤销本地建议编辑并恢复最后一次服务端接纳值。</summary>
    public void RejectSuggestedSupplierChanges()
    {
        SuggestedPurchaseSupplierId = _originalSuggestedPurchaseSupplierId;
        SuggestedPurchaseSupplierCode = _originalSuggestedPurchaseSupplierCode;
        SuggestedPurchaseSupplierName = _originalSuggestedPurchaseSupplierName;
        SuggestedPurchaseSupplierSelection = CreateSuggestedSupplierSelection(
            SuggestedPurchaseSupplierId,
            SuggestedPurchaseSupplierCode,
            SuggestedPurchaseSupplierName);
        OnPropertyChanged(nameof(IsSuggestionDirty));
    }

    /// <summary>把持久化身份与显示快照组合成 Lookup 可识别的合法选择对象。</summary>
    private static ProcurementSupplierCandidateDto? CreateSuggestedSupplierSelection(
        Guid? supplierId,
        string supplierCode,
        string supplierName)
        => supplierId.HasValue
            ? new ProcurementSupplierCandidateDto
            {
                Uid = supplierId.Value,
                SupplierCode = supplierCode,
                SupplierName = supplierName
            }
            : null;
}
