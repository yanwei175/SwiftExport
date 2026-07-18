using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

[Table("OutboundHead")]
public sealed class OutboundDocument : BaseEntity
{
    public string DocumentNo { get; set; } = string.Empty;
    public OutboundDocumentTypeEnum DocumentType { get; set; }
    public byte LifecycleStatus { get; set; }
    public OutboundPostingStatusEnum PostingStatus { get; set; }
    public Guid WarehouseSiteId { get; set; }
    public string? WarehouseCodeSnapshot { get; set; }
    public string? WarehouseNameSnapshot { get; set; }
    public DateTime ShipmentDate { get; set; }
    public Guid? SourcePackingListId { get; set; }
    public string? PackingListNoSnapshot { get; set; }
    public DateTime? PostedDate { get; set; }
    public string? PostedBy { get; set; }
    public DateTime? ReversedDate { get; set; }
    public string? ReversedBy { get; set; }
    public string? ReverseReason { get; set; }
    public Guid CommandId { get; set; }
}
