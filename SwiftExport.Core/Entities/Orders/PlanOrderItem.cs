using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Orders;

public class PlanOrderItem : BaseEntity
{
    public string? PlanOrderNo { get; set; } = default!;
    public string? SoNo { get; set; }
    public long? SoLineID { get; set; }
    public string? SKU { get; set; }
    public decimal QTY { get; set; } = default!;
    public string? DeliveryTo { get; set; }
    public string? CustomerCode { get; set; }
    public string? Note { get; set; }

    public string? SupplierCode { get; set; }
}
