using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

[Table("Inventory")]
public sealed class InventoryBalance : BaseEntity
{
    public Guid WarehouseSiteId { get; set; }
    public Guid ProductId { get; set; }
    public string? SkuSnapshot { get; set; }
    public string? UnitSnapshot { get; set; }
    public decimal OnHandQty { get; set; }
    public long LastLedgerSequence { get; set; }
}
