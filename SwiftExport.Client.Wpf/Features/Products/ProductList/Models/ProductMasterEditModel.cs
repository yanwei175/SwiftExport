using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>主型号详情头部编辑模型，独立维护头部脏状态和校验。</summary>
public sealed partial class ProductMasterEditModel : BaseLineModel<ProductMasterDetailDto>
{
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
    /// <summary>获取或设置默认装箱数；新建初始为 1，空值表示未设置。</summary>
    [ObservableProperty] public partial int? DefaultPcsPerPackage { get; set; } = 1;
    /// <summary>获取或设置默认净重。</summary>
    [ObservableProperty] public partial decimal? DefaultNetWeight { get; set; }
    /// <summary>获取或设置默认毛重。</summary>
    [ObservableProperty] public partial decimal? DefaultGrossWeight { get; set; }
    /// <summary>获取或设置默认体积。</summary>
    [ObservableProperty] public partial decimal? DefaultCbm { get; set; }
    /// <summary>获取或设置属性模板字典明细身份；空值表示未设置模板。</summary>
    [ObservableProperty] public partial Guid? ProductAttributeProfileId { get; set; }
    /// <summary>获取或设置模板显示名称，不作为持久化身份。</summary>
    [ObservableProperty] public partial string ProductAttributeProfileName { get; set; } = string.Empty;
    /// <summary>获取或设置产品系列字典明细身份；为空表示未设置。</summary>
    [ObservableProperty] public partial Guid? ProductSeriesId { get; set; }
    /// <summary>系列的实时显示名称，不作为写入身份。</summary>
    [ObservableProperty] public partial string ProductSeriesName { get; set; } = string.Empty;
    /// <summary>获取或设置分类标识。</summary>
    [ObservableProperty] public partial Guid CategoryId { get; set; }
    /// <summary>获取或设置分类显示名称。</summary>
    [ObservableProperty] public partial string CategoryName { get; set; } = string.Empty;
    /// <summary>获取或设置默认供应商标识。</summary>
    [ObservableProperty] public partial Guid DefaultSupplierId { get; set; }
    /// <summary>获取或设置默认供应商显示名称。</summary>
    [ObservableProperty] public partial string DefaultSupplierName { get; set; } = string.Empty;

    /// <summary>验证主型号编码和可选默认装率；装率为空合法，有值必须为正整数。</summary>
    public override string? ValidateRow()
    {
        if (string.IsNullOrWhiteSpace(MasterSku)) return "主型号编码不能为空。";
        if (DefaultPcsPerPackage is <= 0) return "默认每箱装率有值时必须为正整数。";
        return null;
    }

    /// <summary>转换为主型号写入数据。</summary>
    public ProductMasterWriteDto ToWriteDto() => this.Adapt<ProductMasterWriteDto>();

    /// <summary>从详情契约载入头部字段。</summary>
    protected override void LoadFromDto(ProductMasterDetailDto dto) => dto.Adapt(this);

    /// <summary>转换为详情头部契约，用于基线和回滚。</summary>
    public override ProductMasterDetailDto ToDto() => this.Adapt<ProductMasterDetailDto>();

    /// <summary>字典名称刷新不产生业务修改；系列身份仍参与草稿跟踪。</summary>
    protected override bool IsTrackingIgnoredProperty(string? propertyName) =>
        base.IsTrackingIgnoredProperty(propertyName) || propertyName == nameof(ProductSeriesName);
}
