using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>产品主型号实体，保存主型号层级的默认描述、分类和标签。</summary>
[Table("product_master")]
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

    /// <summary>类型。</summary>
    public string? ProductType { get; set; }

    /// <summary>标签属性用来归类。</summary>
    public string? ProductTag { get; set; }

    /// <summary>分类唯一标识。</summary>
    public Guid CategoryId { get; set; }

    /// <summary>默认供应商标识；使用逻辑外键，由应用服务校验。</summary>
    public Guid DefaultSupplierId { get; set; }
}
