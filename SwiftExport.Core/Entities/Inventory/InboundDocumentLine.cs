using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

/// <summary>新入库明细，兼容映射 dbo.PurchaseReceiveLine。</summary>
[Table("PurchaseReceiveLine")]
public sealed class InboundDocumentLine : BaseEntity
{
    public Guid InboundDocumentId { get; set; }
    public int LineNumber { get; set; }
    public Guid ProductId { get; set; }
    public string SkuSnapshot { get; set; } = string.Empty;
    public string? DescriptionSnapshot { get; set; }
    public string? UnitSnapshot { get; set; }
    public decimal Quantity { get; set; }
    public InboundSourceTypeEnum SourceType { get; set; }
    public Guid SourceUid { get; set; }
    public Guid? DemandExecutionLinkId { get; set; }
    public Guid? NetDemandAllocationId { get; set; }
    public Guid? GrossDemandId { get; set; }
    public string? SalesOrderNoSnapshot { get; set; }
    public int? SalesOrderLineNumberSnapshot { get; set; }
    public Guid? OriginalLineId { get; set; }
}
