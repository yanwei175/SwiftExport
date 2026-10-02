namespace SwiftExport.Core.Models.Procurement;

/// <summary>同来源活动贡献的实际执行目标；用于校验客供件送达供应商，不作为页面契约。</summary>
public sealed class ProcurementAssociationTarget
{
    public Guid LineId { get; set; }
    public Guid HeadId { get; set; }
    public Guid SupplierId { get; set; }
    public int HeadVersion { get; set; }
    public int LineVersion { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? DeliveryTargetSupplierId { get; set; }
}
