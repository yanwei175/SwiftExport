using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Relations;

public class SupplierProduct : BaseEntity
{
    public string? ProductSku { get; set; }
    public string? SupplierCode { get; set; }
    public string? SupplierSku { get; set; }
    public string? SupplierSkuDescriptionEn { get; set; }
    public string? SupplierSkuFeaturesZh { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxPrice { get; set; }

}
