using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Relations;

public class SupplierProduct : BaseEntity
{
    public string? ProductSKU { get; set; }
    public string? SupplierCode { get; set; }
    public string? SSKU { get; set; }
    public string? SDescription { get; set; }
    public string? SFeatures { get; set; }
    public decimal RMBPrice1 { get; set; }
    public decimal RMBPrice2 { get; set; }

}
