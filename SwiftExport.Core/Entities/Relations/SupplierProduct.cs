using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Relations;

[Table("SupplierProduct")]
public class SupplierProduct : BaseEntity
{
    public Guid SupplierId { get; set; }
    public required string SupplierCode { get; set; }
    public Guid SkuId { get; set; }
    public required string ProductSku { get; set; }
    public required string SupplierSku { get; set; }
    public string? SupplierSkuDescriptionZh { get; set; }
    public string? SupplierSkuFeaturesZh { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? TaxPrice { get; set; }
}
