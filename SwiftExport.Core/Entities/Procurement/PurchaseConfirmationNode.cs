using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;
namespace SwiftExport.Core.Entities.Procurement;
/// <summary>按完整路径保存确认 BOM 节点和客供件消耗目标，保留原始比例供审计与后续执行。</summary>
[Table("PurchaseConfirmationNode")]
public sealed class PurchaseConfirmationNode : BaseEntity
{
    public Guid ConfirmationLineId { get; set; }
    public string Path { get; set; } = "";
    public Guid RootProductId { get; set; }
    public Guid ParentProductId { get; set; }
    public Guid ComponentProductId { get; set; }
    public Guid BomLineId { get; set; }
    public string ParentBomNo { get; set; } = "";
    public int ParentBomVersion { get; set; }
    public string ParentSkuSnapshot { get; set; } = "";
    public string ComponentSkuSnapshot { get; set; } = "";
    public string UnitSnapshot { get; set; } = "";
    public string MaterialType { get; set; } = "";
    public string SupplyType { get; set; } = "";
    public int Numerator { get; set; }
    public int Denominator { get; set; }
    public decimal CumulativeRatio { get; set; }
    public bool IsCustomerSupplied { get; set; }
    public Guid? ConsumptionWarehouseSiteId { get; set; }
    public string? WarehouseCodeSnapshot { get; set; }
    public string? WarehouseNameSnapshot { get; set; }
    public Guid? ConsumptionSupplierId { get; set; }
    public string? SupplierCodeSnapshot { get; set; }
    public string? SupplierNameSnapshot { get; set; }
}
