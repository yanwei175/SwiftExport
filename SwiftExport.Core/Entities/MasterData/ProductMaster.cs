using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>产品主型号实体，保存默认描述、分类、属性模板及产品系列身份。</summary>
[Table("product_master")]
[DbIndex("IX_ProductMaster_AttributeProfile", nameof(ProductAttributeProfileId), Filter = "\"is_deleted\" = FALSE AND \"product_attribute_profile_id\" IS NOT NULL")]
[LogicalReference(nameof(ProductAttributeProfileId), typeof(SysDictData))]
[DbIndex("IX_ProductMaster_Series", nameof(ProductSeriesId), Filter = "\"is_deleted\" = FALSE AND \"product_series_id\" IS NOT NULL")]
[LogicalReference(nameof(ProductSeriesId), typeof(SysDictData))]
public partial class ProductMaster : BaseEntity
{
    /// <summary>主型号业务编码；未删除记录内唯一。</summary>
    public string MasterSku { get; set; } = default!;

    // === 模具属性 (作为新增 SKU 时的默认值) ===
    /// <summary>英文描述。</summary>
    public string? DefaultDescriptionEn { get; set; }
    /// <summary>中文描述。</summary>
    public string? DefaultDescriptionZh { get; set; }
    /// <summary>英文默认配置。</summary>
    public string? DefaultFeaturesEn { get; set; }
    /// <summary>中文默认配置。</summary>
    public string? DefaultFeaturesZh { get; set; }
    /// <summary>默认每箱装率（PCS/箱）；为空表示未设置，有值时必须为正整数。</summary>
    public int? DefaultPcsPerPackage { get; set; }

    /// <summary>每箱净重(KG)</summary>
    public decimal? DefaultNetWeight { get; set; }

    /// <summary>每箱毛重(KG)</summary>
    public decimal? DefaultGrossWeight { get; set; }

    /// <summary>每箱体积</summary>
    public decimal? DefaultCbm { get; set; }

    /// <summary>产品属性模板的字典明细身份；为空时不提供标签候选。</summary>
    public Guid? ProductAttributeProfileId { get; set; }

    /// <summary>产品系列字典明细身份；为空表示未设置，改名保持引用，不参与标签选择。</summary>
    public Guid? ProductSeriesId { get; set; }

    /// <summary>分类唯一标识。</summary>
    public Guid CategoryId { get; set; }

    /// <summary>默认供应商标识；使用逻辑外键，由应用服务校验。</summary>
    public Guid DefaultSupplierId { get; set; }
}
