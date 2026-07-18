using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

[Table("OutboundLine")]
public sealed class OutboundDocumentLine : BaseEntity
{
    public Guid OutboundDocumentId { get; set; }
    public int LineNumber { get; set; }
    public Guid ProductId { get; set; }
    public string SkuSnapshot { get; set; } = string.Empty;
    public string? DescriptionSnapshot { get; set; }
    public string? UnitSnapshot { get; set; }
    public decimal Quantity { get; set; }
    public Guid PackingListContributionId { get; set; }
    public Guid NetDemandAllocationId { get; set; }
    public Guid? GrossDemandId { get; set; }
    public string? SalesOrderNoSnapshot { get; set; }
    public Guid CommandItemId { get; set; }
    public Guid? OriginalLineId { get; set; }
}
