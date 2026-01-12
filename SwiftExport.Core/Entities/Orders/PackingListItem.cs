using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Orders;

public class PackingListItem : BaseEntity
{
    public string? PLNo { get; set; } = default!;
    public string? SKU { get; set; }
    public string? CSku { get; set; }
    public string? LinkSku { get; set; }
    public string? CDescription { get; set; }
    public string? CFinish { get; set; }
    public string? HSCode { get; set; }
    public string? SoNo { get; set; }
    public string? CustomerPoNo { get; set; }
    public decimal? QTY { get; set; }
    public decimal? PcsOfCarton { get; set; }
    public decimal? NwOfCarton { get; set; }
    public decimal? GwOfCarton { get; set; }
    public decimal? CbmOfCarton { get; set; }
    public decimal? PcsSubTotal { get; set; }
    public decimal? NwSubTotal { get; set; }
    public decimal? GwSubTotal { get; set; }
    public decimal? CbmSubTotal { get; set; }
    public string? SubNote { get; set; }
}
