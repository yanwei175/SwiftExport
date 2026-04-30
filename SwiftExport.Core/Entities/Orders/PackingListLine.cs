using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// Packing List line. Each line represents finished SKU quantity and keeps source PO/SO line references.
/// </summary>
[Table("PackingListLine")]
public class PackingListLine : BaseEntity
{
    public string PackingListNo { get; set; } = string.Empty;
    public int? PackingListHeadId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? CustomerSku { get; set; }
    public string? Description { get; set; }
    public decimal Qty { get; set; }
    public string? Unit { get; set; }
    public int? PoLineId { get; set; }
    public long? SoLineId { get; set; }
    public string? PoNo { get; set; }
    public string? PurchaseOrderNo { get; set; }
    public string? SalesOrderNo { get; set; }
    public string? Remark { get; set; }
}
