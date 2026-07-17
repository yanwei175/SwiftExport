using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

/// <summary>新入库业务模型，兼容映射 dbo.PurchaseReceiveHead。</summary>
[Table("PurchaseReceiveHead")]
public sealed class InboundDocument : BaseEntity
{
    public string DocumentNo { get; set; } = string.Empty;
    public InboundDocumentTypeEnum DocumentType { get; set; }
    public byte LifecycleStatus { get; set; }
    public InboundPostingStatusEnum PostingStatus { get; set; }
    public Guid WarehouseSiteId { get; set; }
    public string? WarehouseCodeSnapshot { get; set; }
    public string? WarehouseNameSnapshot { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierCodeSnapshot { get; set; }
    public string? SupplierNameSnapshot { get; set; }
    public DateTime ReceiptDate { get; set; }
    public string? Remark { get; set; }
    public DateTime? PostedDate { get; set; }
    public string? PostedBy { get; set; }
    public DateTime? ReversedDate { get; set; }
    public string? ReversedBy { get; set; }
    public string? ReverseReason { get; set; }
    public Guid? SourcePackingListId { get; set; }
    public Guid? CommandId { get; set; }
}
