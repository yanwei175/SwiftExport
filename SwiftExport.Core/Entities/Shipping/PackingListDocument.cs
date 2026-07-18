using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Shipping;

[Table("PackingListHead")]
public sealed class PackingListDocument : BaseEntity
{
    public int LegacyHeadId { get; set; }
    public string PackingListNo { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string? CustomerCodeSnapshot { get; set; }
    public string? CustomerNameSnapshot { get; set; }
    public DateTime PackingDate { get; set; }
    public DateTime ShipmentDate { get; set; }
    public DateTime? Etd { get; set; }
    public DateTime? Eta { get; set; }
    public string? InvoiceNo { get; set; }
    public string? ContainerNo { get; set; }
    public string? SealNo { get; set; }
    public string? ShippingMark { get; set; }
    public string? DeliveryAddressSnapshot { get; set; }
    public PackingListLifecycleStatusEnum LifecycleStatus { get; set; }
    public PackingListPostingStatusEnum PostingStatus { get; set; }
    public DateTime? PostedDate { get; set; }
    public string? PostedBy { get; set; }
    public DateTime? ReversedDate { get; set; }
    public string? ReversedBy { get; set; }
    public string? ReverseReason { get; set; }
    public Guid CreateCommandId { get; set; }
    public string? CreateRequestFingerprint { get; set; }
    public Guid? PostCommandId { get; set; }
    public string? PostPreviewToken { get; set; }
    public Guid? ReverseCommandId { get; set; }
    public string? ReversePreviewToken { get; set; }
    public bool IsLegacyReadOnly { get; set; }
    public string? ReadOnlyReason { get; set; }
}
