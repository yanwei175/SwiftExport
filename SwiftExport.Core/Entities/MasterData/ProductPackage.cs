using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.MasterData;

public class ProductPackage : BaseEntity
{
    public string? ProductSKU { get; set; } = default!;
    public int? PcsOfCarton { get; set; }
    public decimal? NwOfCarton { get; set; }
    public decimal? GwOfCarton { get; set; }
    public decimal? CbmOfCarton { get; set; }
    public string? CartonClass { get; set; }
    public string? CartonType { get; set; }
    public decimal? CartonLength_CM { get; set; }
    public decimal? CartonWidth_CM { get; set; }
    public decimal? CartonHeight_CM { get; set; }
    public string? ShippCartonClass { get; set; }
    public string? ShippCartonType { get; set; }
    public decimal? ShippCartonLength_CM { get; set; }
    public decimal? ShippCartonWidth_CM { get; set; }
    public decimal? ShippCartonHeight_CM { get; set; }
    public string? BoxClass { get; set; }
    public string? BoxType { get; set; }
    public decimal? BoxLength_CM { get; set; }
    public decimal? BoxWidth_CM { get; set; }
    public decimal? BoxHeight_CM { get; set; }

}
