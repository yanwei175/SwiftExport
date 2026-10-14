using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>物料详情私有编辑模型，仅维护详情允许修改的资料；来源、包装和标签选择由详情文档分别持有。</summary>
public sealed partial class ProductEditModel : BaseLineModel<ProductDetailDto>
{
    /// <summary>获取或设置物料编号。</summary>
    [ObservableProperty] public partial string Sku { get; set; } = string.Empty;
    /// <summary>获取或设置产品型号。</summary>
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
    [ObservableProperty] public partial string MaterialType { get; set; } = string.Empty;
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
    /// <summary>验证必须录入的物料编号，其余业务关系由后端在事务中重新校验。</summary>
    public override string? ValidateRow() => string.IsNullOrWhiteSpace(Sku) ? "物料编号不能为空。" : null;
    /// <summary>采集专用详情写契约，不发送来源主型号、模板、包装或 BOM 字段。</summary>
    public ProductDetailWriteDto ToWriteDto() => this.Adapt<ProductDetailWriteDto>();
    /// <summary>接纳完整读快照中的可写字段与选择器显示信息。</summary>
    protected override void LoadFromDto(ProductDetailDto dto) => dto.Adapt(this);
    /// <summary>转换为用于基线及回滚的物料详情快照。</summary>
    public override ProductDetailDto ToDto() => this.Adapt<ProductDetailDto>();
}
