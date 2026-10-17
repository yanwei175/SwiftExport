using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>产品列表可编辑行，保存当前可空关联及独立制造商型号。</summary>
public partial class ProductLineModel : BaseLineModel<ProductDto>
{
    private bool _loadedBomBinding;
    private string _loadedBomNo = string.Empty;
    [ObservableProperty] public partial string Sku { get; set; } = string.Empty;
    [ObservableProperty] public partial string MasterSku { get; set; } = string.Empty;
    /// <summary>物料独立关联的主型号身份；编码仅作联表显示。</summary>
    [ObservableProperty] public partial Guid? ProductMasterId { get; set; }
    [ObservableProperty] public partial string DescriptionEn { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string FeaturesEn { get; set; } = string.Empty;
    [ObservableProperty] public partial string FeaturesZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string Finish { get; set; } = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChangeBomRequirement), nameof(BomRequirementHint), nameof(CanEditBomNo))]
    public partial string MaterialType { get; set; } = string.Empty;
    [ObservableProperty] public partial string Unit { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplyType { get; set; } = string.Empty;
    /// <summary>原样保存正数需求标记，勾选操作才将其设为 0 或 1。</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RequiresBom), nameof(CanChangeBomRequirement), nameof(BomRequirementHint))]
    public partial int BomVersion { get; set; }
    /// <summary>未绑定成品可输入自定义编号，已有绑定禁止直接换号或清号。</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChangeBomRequirement), nameof(BomRequirementHint), nameof(IsBomBindingSaved))]
    public partial string BomNo { get; set; } = string.Empty;
    /// <summary>所有正数均回显为勾选，编辑勾选才写入 0 或 1。</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChangeBomRequirement), nameof(BomRequirementHint), nameof(CanEditBomNo), nameof(IsBomBindingSaved))]
    public partial bool RequiresBom { get; set; }
    /// <summary>未绑定成品可以切换需求，供货类型不影响资格。</summary>
    public bool CanChangeBomRequirement => MaterialType == MaterialTypesEnum.成品 && !_loadedBomBinding;
    /// <summary>编号编辑资格取读取时绑定基线，不因刚输入第一个字符而锁定草稿。</summary>
    public bool CanEditBomNo => MaterialType == MaterialTypesEnum.成品 && RequiresBom && !_loadedBomBinding;
    /// <summary>只有已保存且未被本地换号的绑定可作为组成维护入口。</summary>
    public bool IsBomBindingSaved => _loadedBomBinding && RequiresBom && BomNo == _loadedBomNo;
    /// <summary>为列表勾选项解释只读条件。</summary>
    public string BomRequirementHint => MaterialType != MaterialTypesEnum.成品 ? "非成品只能作为 BOM 子件。"
        : !CanChangeBomRequirement ? "已有 BOM 绑定，不能直接取消。" : "需要时勾选，保存后在 BOM 管理中建立组成。";
    /// <summary>数值标记投影成勾选，既有正数不会仅因回显变成 1。</summary>
    partial void OnBomVersionChanged(int value) => RequiresBom = value > 0;
    /// <summary>仅用户编辑同步同一需求的数值表示，程序加载保留服务器原值。</summary>
    partial void OnRequiresBomChanged(bool value)
    {
        if (!IsSuppressed && value != (BomVersion > 0)) BomVersion = value ? 1 : 0;
    }
    [ObservableProperty] public partial string Note { get; set; } = string.Empty;
    /// <summary>物料自身可自由填写的型号；候选中性产品编码仅作输入建议。</summary>
    [ObservableProperty] public partial string LinkSku { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal CnyPrice { get; set; }
    [ObservableProperty] public partial decimal UsdPrice { get; set; }
    /// <summary>物料单件自重（KG），与外箱重量分开维护。</summary>
    [ObservableProperty] public partial decimal? UnitWeight { get; set; }
    [ObservableProperty] public partial decimal? PcsOfCarton { get; set; }
    [ObservableProperty] public partial decimal? NwOfCarton { get; set; }
    [ObservableProperty] public partial decimal? GwOfCarton { get; set; }
    [ObservableProperty] public partial decimal? CbmOfCarton { get; set; }
    [ObservableProperty] public partial Guid CategoryId { get; set; }
    [ObservableProperty] public partial Guid ColorId { get; set; }
    [ObservableProperty] public partial string CategoryName { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplierName { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid? DefaultSupplierId { get; set; }
    /// <summary>产品建议仓库标识；编辑该值不改变入出库单头的实际仓库。</summary>
    [ObservableProperty] public partial Guid? DefaultWarehouseSiteId { get; set; }
    [ObservableProperty] public partial string DefaultWarehouseCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string DefaultWarehouseName { get; set; } = string.Empty;
    /// <summary>制造商独立于默认采购供应商。</summary>
    [ObservableProperty] public partial Guid? ManufacturerId { get; set; }
    [ObservableProperty] public partial string ManufacturerCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string ManufacturerName { get; set; } = string.Empty;
    /// <summary>允许独立输入的制造商型号。</summary>
    [ObservableProperty] public partial string ManufacturerModel { get; set; } = string.Empty;
    [ObservableProperty] public partial string DefaultSupplierCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string MainImage { get; set; } = string.Empty;

    /// <summary>验证物料列表的必填、非负数值和可空引用输入，错误保留在当前编辑行。</summary>
    public override Task<string?> ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(Sku)) return Task.FromResult<string?>("SKU 不能为空。");
        if (BomVersion < 0) return Task.FromResult<string?>("BOM 版本不能小于 0。");
        if (_loadedBomBinding && !RequiresBom) return Task.FromResult<string?>("已有 BOM 绑定，不能直接取消。");
        if (_loadedBomBinding && BomNo != _loadedBomNo) return Task.FromResult<string?>("已有 BOM 绑定，不能直接换号或清号。");
        if (RequiresBom && MaterialType != MaterialTypesEnum.成品) return Task.FromResult<string?>("只有成品可以需要 BOM。");
        if (!string.IsNullOrWhiteSpace(BomNo) && (!RequiresBom || MaterialType != MaterialTypesEnum.成品))
            return Task.FromResult<string?>("只有已勾选需要 BOM 的成品可以填写编号。");
        if (CnyPrice < 0 || UsdPrice < 0) return Task.FromResult<string?>("价格不能小于 0。");
        if (UnitWeight < 0) return Task.FromResult<string?>("单件自重不能小于 0。");
        if (DefaultWarehouseSiteId == Guid.Empty || ManufacturerId == Guid.Empty)
            return Task.FromResult<string?>("默认仓库或制造商标识不能是空 Guid。");
        if (ManufacturerModel?.Trim().Length > 200)
            return Task.FromResult<string?>("制造商型号不能超过 200 个字符。");
        return Task.FromResult<string?>(null);
    }

    /// <summary>接纳物料资料和已绑定保护基线，加载不规范化原有正数需求标记。</summary>
    protected override void LoadFromDto(ProductDto dto)
    {
        dto.Adapt(this);
        AcceptBomBindingBaseline();
    }

    /// <summary>随读取或成功写回接纳绑定基线，后续刷新失败也不能把已保存编号当作可换号草稿。</summary>
    internal void AcceptBomBindingBaseline()
    {
        _loadedBomBinding = RequiresBom && !string.IsNullOrWhiteSpace(BomNo);
        _loadedBomNo = BomNo;
        OnPropertyChanged(nameof(CanChangeBomRequirement)); OnPropertyChanged(nameof(CanEditBomNo));
        OnPropertyChanged(nameof(IsBomBindingSaved)); OnPropertyChanged(nameof(BomRequirementHint));
    }

    /// <summary>冻结当前行资料，并明确声明自重更新，使清空与旧客户端省略可区分。</summary>
    public override ProductDto ToDto()
    {
        var dto = this.Adapt<ProductDto>();
        dto.UpdateUnitWeight = true;
        return dto;
    }
}
