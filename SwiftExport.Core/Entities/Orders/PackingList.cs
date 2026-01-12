using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Orders;

public class PackingList : BaseEntity
{
    public string? PLNo { get; set; } = default!;
    public DateTime? PLDate { get; set; }
    public string? CustomerCode { get; set; }
    public string? ShipTo { get; set; }
    public string? SoNo { get; set; }
    public DateTime? SoDate { get; set; }
    public string? TransportNo { get; set; }
    public string? SealNo { get; set; }
    public DateTime? ETD { get; set; }
    public DateTime? ETA { get; set; }
    public string? Attention { get; set; }
    public string? PLFolder { get; set; }
}
