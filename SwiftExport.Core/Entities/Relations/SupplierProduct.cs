using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Relations;

/// <summary>供应商产品关系实体，保存供应商与公司产品之间的采购编码、描述和价格约定。</summary>
[Table("SupplierProduct")]
public partial class SupplierProduct : BaseEntity
{
    /// <summary>关联的供应商主数据唯一标识。</summary>
    public Guid SupplierId { get; set; }
    /// <summary>该实体保存的供应商业务编码。</summary>
    public required string SupplierCode { get; set; }
    /// <summary>关联的公司产品主数据唯一标识。</summary>
    public Guid SkuId { get; set; }
    /// <summary>该实体保存的产品编码。</summary>
    public required string ProductSku { get; set; }
    /// <summary>该实体保存的供应商产品编码。</summary>
    public required string SupplierSku { get; set; }
    /// <summary>该实体保存的供应商产品中文描述。</summary>
    public string? SupplierSkuDescriptionZh { get; set; }
    /// <summary>该实体保存的供应商产品中文规格。</summary>
    public string? SupplierSkuFeaturesZh { get; set; }
    /// <summary>该实体保存的未税采购价。</summary>
    public decimal? UnitPrice { get; set; }
    /// <summary>该实体保存的含税采购价。</summary>
    public decimal? TaxPrice { get; set; }
}
