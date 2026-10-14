using SwiftExport.Contracts.DTOs.Products.ProductMasters;

namespace SwiftExportClient.Features.Products.Components.Models;

/// <summary>
/// 主型号分页结果的只读候选外观，保留原 DTO 并提供 Lookup 所需顶层属性，不复制或维护第二份数据。
/// 调用方应把 Source 当作查询快照使用；此模型不提供编辑状态或属性变更通知。
/// </summary>
public sealed partial class ProductMasterLookupItemModel
{
    /// <summary>包装现有分页结果；拒绝空来源，不额外查询详情。</summary>
    public ProductMasterLookupItemModel(ProductMasterListItemDto source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>完整原始分页 DTO，供调用方访问未来新增字段；使用时不应修改该查询快照。</summary>
    public ProductMasterListItemDto Source { get; }

    /// <summary>获取主型号唯一标识。</summary>
    public Guid Uid => Source.Uid;

    /// <summary>获取并发版本。</summary>
    public int Version => Source.Version;

    /// <summary>获取软删除标记。</summary>
    public bool IsDeleted => Source.IsDeleted;

    /// <summary>获取创建时间。</summary>
    public DateTime? CreatedDate => Source.CreatedDate;

    /// <summary>获取创建人。</summary>
    public string? CreatedBy => Source.CreatedBy;

    /// <summary>获取更新时间。</summary>
    public DateTime? UpdatedDate => Source.UpdatedDate;

    /// <summary>获取更新人。</summary>
    public string? UpdatedBy => Source.UpdatedBy;

    /// <summary>获取主型号编码。</summary>
    public string? MasterSku => Source.Data?.MasterSku;

    /// <summary>获取默认英文描述。</summary>
    public string? DefaultDescriptionEn => Source.Data?.DefaultDescriptionEn;

    /// <summary>获取默认中文描述。</summary>
    public string? DefaultDescriptionZh => Source.Data?.DefaultDescriptionZh;

    /// <summary>获取默认英文配置。</summary>
    public string? DefaultFeaturesEn => Source.Data?.DefaultFeaturesEn;

    /// <summary>获取默认中文配置。</summary>
    public string? DefaultFeaturesZh => Source.Data?.DefaultFeaturesZh;

    /// <summary>获取属性模板字典明细身份。</summary>
    public Guid? ProductAttributeProfileId => Source.Data?.ProductAttributeProfileId;
    /// <summary>获取属性模板显示名称。</summary>
    public string? ProductAttributeProfileName => Source.ProductAttributeProfileName;

    /// <summary>获取产品系列字典明细身份；为空表示未设置。</summary>
    public Guid? ProductSeriesId => Source.Data?.ProductSeriesId;
    /// <summary>获取系列字典的实时名称，不以名称承担引用身份。</summary>
    public string? ProductSeriesName => Source.ProductSeriesName;

    /// <summary>获取默认每箱装率；来源未设置时保留空值，不生成默认装率。</summary>
    public int? DefaultPcsPerPackage => Source.Data?.DefaultPcsPerPackage;

    /// <summary>获取默认每箱净重（KG）。</summary>
    public decimal? DefaultNetWeight => Source.Data?.DefaultNetWeight;

    /// <summary>获取默认每箱毛重（KG）。</summary>
    public decimal? DefaultGrossWeight => Source.Data?.DefaultGrossWeight;

    /// <summary>获取默认每箱体积（CBM）。</summary>
    public decimal? DefaultCbm => Source.Data?.DefaultCbm;

    /// <summary>获取分类标识。</summary>
    public Guid CategoryId => Source.Data?.CategoryId ?? Guid.Empty;

    /// <summary>获取分类名称。</summary>
    public string? CategoryName => Source.CategoryName;

    /// <summary>获取默认供应商标识。</summary>
    public Guid DefaultSupplierId => Source.Data?.DefaultSupplierId ?? Guid.Empty;

    /// <summary>获取默认供应商名称。</summary>
    public string? DefaultSupplierName => Source.DefaultSupplierName;

    /// <summary>获取默认主图路径。</summary>
    public string? MainImagePath => Source.MainImagePath;

    /// <summary>获取默认主图中性产品的美元售价。</summary>
    public decimal? DefaultSalesPriceUsd => Source.DefaultSalesPriceUsd;

    /// <summary>获取默认主图中性产品的人民币采购价。</summary>
    public decimal? DefaultPurchasePriceCny => Source.DefaultPurchasePriceCny;
}

