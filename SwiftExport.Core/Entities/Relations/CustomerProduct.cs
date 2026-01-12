using SwiftExport.Core.Common;
namespace SwiftExport.Core.Entities.Relations;

public class CustomerProduct : BaseEntity
{

    public string? CustomerCode { get; set; }
    public string? CSku { get; set; }
    public string? LinkSku { get; set; }
    public string? ProductSku { get; set; }
    public string? CSeries { get; set; }
    public string? CDescription { get; set; }
    public string? CFinish { get; set; }
    public string? CFeatures { get; set; }
    public string? SpecFeatures { get; set; }
    public decimal RmbPrice { get; set; }
    public decimal UsdPrice { get; set; }
    public string? Barcode1 { get; set; }
    public string? Barcode2 { get; set; }
    public string? Note { get; set; }

}

