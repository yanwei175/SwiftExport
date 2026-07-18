using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Shipping;

[Table("PackingListLine")]
public sealed class PackingListContributionLine : BaseEntity
{
    public int LegacyPackingListHeadId { get; set; }
    public Guid PackingListDocumentId { get; set; }
    public int LineNumber { get; set; }
    public Guid ProductId { get; set; }
    public string SkuSnapshot { get; set; } = string.Empty;
    public string? CustomerSkuSnapshot { get; set; }
    public string? DescriptionSnapshot { get; set; }
    public string? UnitSnapshot { get; set; }
    public decimal Quantity { get; set; }
    public decimal? PackageCount { get; set; }
    public decimal? NetWeight { get; set; }
    public decimal? GrossWeight { get; set; }
    public decimal? Volume { get; set; }
    public string? Marks { get; set; }
    public string? Note { get; set; }
    public Guid GrossDemandId { get; set; }
    public Guid NetDemandId { get; set; }
    public Guid NetDemandAllocationId { get; set; }
    public Guid? DemandExecutionLinkId { get; set; }
    public Guid? SalesOrderId { get; set; }
    public Guid? SalesOrderLineId { get; set; }
    public string? SalesOrderNoSnapshot { get; set; }
    public int? SalesOrderLineNumberSnapshot { get; set; }
    public Guid SourceSupplierId { get; set; }
    public string? SourceSupplierNameSnapshot { get; set; }
    public Guid WarehouseSiteId { get; set; }
    public string? WarehouseCodeSnapshot { get; set; }
    public string? WarehouseNameSnapshot { get; set; }
    public DateTime? SnapshotRequiredDate { get; set; }
    public byte SupplyType { get; set; }
    public Guid CommandItemId { get; set; }
}
