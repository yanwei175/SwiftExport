using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>主型号管理列表的可编辑行，负责脏状态、并发字段和本地行校验。</summary>
public sealed partial class ProductMasterLineModel : BaseLineModel<ProductMasterListItemDto>
{
    /// <summary>获取客户端稳定行键，用于批量保存后原位回填标识和版本。</summary>
    public string ClientKey { get; } = Guid.NewGuid().ToString("N");

    /// <summary>获取或设置主型号编码。</summary>
    [ObservableProperty] public partial string MasterSku { get; set; } = string.Empty;
    /// <summary>获取或设置默认英文描述。</summary>
    [ObservableProperty] public partial string DefaultDescriptionEn { get; set; } = string.Empty;
    /// <summary>获取或设置默认中文描述。</summary>
    [ObservableProperty] public partial string DefaultDescriptionZh { get; set; } = string.Empty;
    /// <summary>获取或设置默认英文配置。</summary>
    [ObservableProperty] public partial string DefaultFeaturesEn { get; set; } = string.Empty;
    /// <summary>获取或设置默认中文配置。</summary>
    [ObservableProperty] public partial string DefaultFeaturesZh { get; set; } = string.Empty;
    /// <summary>获取或设置默认装箱数。</summary>
    [ObservableProperty] public partial int DefaultPcsPerPackage { get; set; } = 1;
    /// <summary>获取或设置默认净重。</summary>
    [ObservableProperty] public partial decimal? DefaultNetWeight { get; set; }
    /// <summary>获取或设置默认毛重。</summary>
    [ObservableProperty] public partial decimal? DefaultGrossWeight { get; set; }
    /// <summary>获取或设置默认体积。</summary>
    [ObservableProperty] public partial decimal? DefaultCbm { get; set; }
    /// <summary>获取或设置产品类型。</summary>
    [ObservableProperty] public partial string ProductType { get; set; } = string.Empty;
    /// <summary>获取或设置产品标签。</summary>
    [ObservableProperty] public partial string ProductTag { get; set; } = string.Empty;
    /// <summary>获取或设置分类标识。</summary>
    [ObservableProperty] public partial Guid CategoryId { get; set; }
    /// <summary>获取或设置分类显示名称。</summary>
    [ObservableProperty] public partial string CategoryName { get; set; } = string.Empty;
    /// <summary>获取或设置默认供应商标识。</summary>
    [ObservableProperty] public partial Guid DefaultSupplierId { get; set; }
    /// <summary>获取或设置默认供应商显示名称。</summary>
    [ObservableProperty] public partial string DefaultSupplierName { get; set; } = string.Empty;
    /// <summary>获取中性产品主图路径；该字段仅用于列表展示。</summary>
    [ObservableProperty] public partial string MainImagePath { get; set; } = string.Empty;
    /// <summary>获取默认主图中性产品的美元销售价；该字段仅用于列表展示。</summary>
    [ObservableProperty] public partial decimal? DefaultSalesPriceUsd { get; set; }
    /// <summary>获取默认主图中性产品的人民币采购价；该字段仅用于列表展示。</summary>
    [ObservableProperty] public partial decimal? DefaultPurchasePriceCny { get; set; }

    /// <summary>验证主型号行唯一需要在客户端阻断的必填字段。</summary>
    public override string? ValidateRow()
    {
        if (string.IsNullOrWhiteSpace(MasterSku)) return "主型号编码不能为空。";
        return null;
    }

    /// <summary>转换为主型号写入数据。</summary>
    public ProductMasterWriteDto ToWriteDto() => this.Adapt<ProductMasterWriteDto>();

    /// <summary>从列表契约载入可编辑字段。</summary>
    protected override void LoadFromDto(ProductMasterListItemDto dto) => dto.Adapt(this);

    /// <summary>转换为列表契约，用于基线和回滚。</summary>
    public override ProductMasterListItemDto ToDto() => this.Adapt<ProductMasterListItemDto>();
}
