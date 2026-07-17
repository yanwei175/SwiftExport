using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

[Table("Warehouse")]
public sealed class WarehouseSite : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public byte SiteType { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierCodeSnapshot { get; set; }
    public string? SupplierNameSnapshot { get; set; }
    public bool IsActive { get; set; }
}
