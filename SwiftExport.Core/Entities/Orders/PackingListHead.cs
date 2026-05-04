using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

/// <summary>
/// Packing List header. The document links purchase receipt, sales shipment, and BOM component write-off.
/// </summary>
[Table("PackingListHead")]
public class PackingListHead : BaseEntity
{
    public string PackingListNo { get; set; } = string.Empty;
    public string? PoNo { get; set; }
    public string? PurchaseOrderNo { get; set; }
    public string? SalesOrderNo { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public DateTime? PackingDate { get; set; }
    public bool IsPosted { get; set; }
    public DateTime? PostedDate { get; set; }
    public string? PostedBy { get; set; }
    public string? ReceiveNo { get; set; }
    public string? Remark { get; set; }
}
