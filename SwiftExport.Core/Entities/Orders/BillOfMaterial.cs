using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Orders;

public class BillOfMaterial : BaseEntity

{
    public string BomNo { get; set; } = default!;
    public string ProductSKU { get; set; } = default!;
    public decimal Qty { get; set; } = default!;
    public Guid SkuId { get; set; } = default!;
    public string SubType { get; set; } = default!;
}
