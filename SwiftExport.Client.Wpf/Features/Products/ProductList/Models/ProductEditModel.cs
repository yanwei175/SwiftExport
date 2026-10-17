using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>物料详情私有编辑模型，维护独立资料、自由型号与可空主型号引用；包装和标签选择由详情文档分别持有。</summary>
public sealed partial class ProductEditModel : BaseLineModel<ProductDetailDto>
{
    private bool _loadedRequiresBom;
    private bool _loadedBomBinding;
    /// <summary>获取或设置物料编号。</summary>
    [ObservableProperty] public partial string Sku { get; set; } = string.Empty;
    /// <summary>获取或设置独立主型号身份，不通过中性产品推导。</summary>
    [ObservableProperty] public partial Guid? ProductMasterId { get; set; }
    /// <summary>获取或设置主型号实时显示编码，不作为保存身份。</summary>
    [ObservableProperty] public partial string MasterSku { get; set; } = string.Empty;
    /// <summary>获取或设置物料自身型号，允许自由录入，不依赖中性产品身份。</summary>
    [ObservableProperty] public partial string LinkSku { get; set; } = string.Empty;
    /// <summary>获取或设置英文描述。</summary>
    [ObservableProperty] public partial string DescriptionEn { get; set; } = string.Empty;
    /// <summary>获取或设置中文描述。</summary>
    [ObservableProperty] public partial string DescriptionZh { get; set; } = string.Empty;
    /// <summary>获取或设置英文配置。</summary>
    [ObservableProperty] public partial string FeaturesEn { get; set; } = string.Empty;
    /// <summary>获取或设置中文配置。</summary>
    [ObservableProperty] public partial string FeaturesZh { get; set; } = string.Empty;
    /// <summary>获取或设置颜色身份。</summary>
    [ObservableProperty] public partial Guid? ColorId { get; set; }
    /// <summary>获取或设置颜色显示名称。</summary>
    [ObservableProperty] public partial string Finish { get; set; } = string.Empty;
    /// <summary>获取或设置物料类型。</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChangeBomRequirement), nameof(BomRequirementHint))]
    public partial string MaterialType { get; set; } = string.Empty;
    /// <summary>保留服务器的 BOM 有效标记，仅明确切换需求时写入 0 或 1。</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RequiresBom), nameof(CanChangeBomRequirement), nameof(BomRequirementHint))]
    public partial int BomVersion { get; set; }
    /// <summary>当前绑定编号只读展示，不随取消需求清空。</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChangeBomRequirement), nameof(BomRequirementHint))]
    public partial string BomNo { get; set; } = string.Empty;
    /// <summary>所有正数回显为勾选，只有明确编辑勾选才同步有效标记。</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChangeBomRequirement), nameof(BomRequirementHint))]
    public partial bool RequiresBom { get; set; }
    /// <summary>只有未绑定的成品可以切换需求，包括采购成品。</summary>
    public bool CanChangeBomRequirement => MaterialType == MaterialTypesEnum.成品 && !(RequiresBom && !string.IsNullOrWhiteSpace(BomNo));
    /// <summary>解释禁用原因，已有空组成绑定同样不能直接取消。</summary>
    public string BomRequirementHint => MaterialType != MaterialTypesEnum.成品 ? "只有成品可以勾选需要 BOM。"
        : !CanChangeBomRequirement ? "已绑定 BOM，不能直接取消需要 BOM。" : "勾选后可到 BOM 管理页建立或绑定。";
    /// <summary>回显标记时只投影勾选状态，保持原有正数不被改为 1。</summary>
    partial void OnBomVersionChanged(int value) => RequiresBom = value > 0;
    /// <summary>同步同一需求的布尔与数值表示，加载期间不把服务器值规范化。</summary>
    partial void OnRequiresBomChanged(bool value)
    {
        if (!IsSuppressed && value != (BomVersion > 0)) BomVersion = value ? 1 : 0;
    }
    /// <summary>获取或设置供货方式。</summary>
    [ObservableProperty] public partial string SupplyType { get; set; } = string.Empty;
    /// <summary>获取或设置基础单位英文代码。</summary>
    [ObservableProperty] public partial string Unit { get; set; } = string.Empty;
    /// <summary>获取或设置人民币参考价。</summary>
    [ObservableProperty] public partial decimal? CnyPrice { get; set; }
    /// <summary>获取或设置美元参考价。</summary>
    [ObservableProperty] public partial decimal? UsdPrice { get; set; }
    /// <summary>获取或设置单品重量（KG）。</summary>
    [ObservableProperty] public partial decimal? UnitWeight { get; set; }
    /// <summary>获取或设置单品体积（CBM）。</summary>
    [ObservableProperty] public partial decimal? UnitCbm { get; set; }
    /// <summary>获取或设置默认供应商身份。</summary>
    [ObservableProperty] public partial Guid? DefaultSupplierId { get; set; }
    /// <summary>获取或设置默认供应商编码。</summary>
    [ObservableProperty] public partial string DefaultSupplierCode { get; set; } = string.Empty;
    /// <summary>获取或设置默认供应商名称。</summary>
    [ObservableProperty] public partial string SupplierName { get; set; } = string.Empty;
    /// <summary>获取或设置制造商身份。</summary>
    [ObservableProperty] public partial Guid? ManufacturerId { get; set; }
    /// <summary>获取或设置制造商编码。</summary>
    [ObservableProperty] public partial string ManufacturerCode { get; set; } = string.Empty;
    /// <summary>获取或设置制造商名称。</summary>
    [ObservableProperty] public partial string ManufacturerName { get; set; } = string.Empty;
    /// <summary>获取或设置制造商型号。</summary>
    [ObservableProperty] public partial string ManufacturerModel { get; set; } = string.Empty;
    /// <summary>获取或设置参考仓库身份。</summary>
    [ObservableProperty] public partial Guid? DefaultWarehouseSiteId { get; set; }
    /// <summary>获取或设置参考仓库编码。</summary>
    [ObservableProperty] public partial string DefaultWarehouseCode { get; set; } = string.Empty;
    /// <summary>获取或设置参考仓库名称。</summary>
    [ObservableProperty] public partial string DefaultWarehouseName { get; set; } = string.Empty;
    /// <summary>获取或设置原图完整地址或本地路径。</summary>
    [ObservableProperty] public partial string MainImage { get; set; } = string.Empty;
    /// <summary>获取或设置备注。</summary>
    [ObservableProperty] public partial string Note { get; set; } = string.Empty;
    /// <summary>验证编号和成品需求资格，绑定与并发规则仍由后端在事务中重新校验。</summary>
    public override string? ValidateRow() => string.IsNullOrWhiteSpace(Sku) ? "物料编号不能为空。"
        : _loadedBomBinding && !RequiresBom ? "已有 BOM 绑定，不能直接取消。"
        : RequiresBom && MaterialType != MaterialTypesEnum.成品 ? "只有成品可以需要 BOM。" : null;
    /// <summary>仅需求勾选相对读取基线改变时提交意图，其他保存保留服务器原有正数。</summary>
    public ProductDetailWriteDto ToWriteDto()
    {
        var dto = this.Adapt<ProductDetailWriteDto>();
        dto.RequiresBom = RequiresBom == _loadedRequiresBom ? null : RequiresBom;
        return dto;
    }
    /// <summary>接纳完整读快照中的可写字段与选择器显示信息。</summary>
    protected override void LoadFromDto(ProductDetailDto dto)
    {
        dto.Adapt(this);
        _loadedRequiresBom = dto.BomVersion > 0;
        _loadedBomBinding = dto.BomVersion > 0 && !string.IsNullOrWhiteSpace(dto.BomNo);
    }
    /// <summary>转换为用于基线及回滚的物料详情快照。</summary>
    public override ProductDetailDto ToDto() => this.Adapt<ProductDetailDto>();
}
