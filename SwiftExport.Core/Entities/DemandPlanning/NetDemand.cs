using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.DemandPlanning;

/// <summary>
/// 毛需求一次拆解版本中的计划执行行。兼容期继续落在 DependentDemand 物理表。
/// </summary>
[Table("DependentDemand")]
public sealed class NetDemand : BaseEntity
{
    public Guid GrossDemandId { get; set; }
    public int DecompositionRevision { get; set; }
    public int LineNumber { get; set; }
    public NetDemandLifecycleStatusEnum LifecycleStatus { get; set; } = NetDemandLifecycleStatusEnum.Active;

    public Guid? ParentNetDemandId { get; set; }
    public Guid RootNetDemandId { get; set; }
    public Guid? DeliveryTargetNetDemandId { get; set; }
    public DemandDeliveryModeEnum DeliveryMode { get; set; } = DemandDeliveryModeEnum.Standard;

    public Guid? ProductId { get; set; }
    public Guid? BomLineId { get; set; }
    public int? BomVersion { get; set; }
    public string? BomPath { get; set; }
    public string? MaterialTypeSnapshot { get; set; }
    public string SupplyTypeSnapshot { get; set; } = string.Empty;
    public string SkuSnapshot { get; set; } = string.Empty;
    public string? ParentSkuSnapshot { get; set; }
    public string? CustomerSkuSnapshot { get; set; }
    public string? DescriptionSnapshot { get; set; }
    public string? ColorSnapshot { get; set; }
    public string? SpecificationSnapshot { get; set; }

    public decimal GrossQty { get; set; }
    public decimal NetQty { get; set; }
    public DateTime? RequiredDate { get; set; }
    public Guid? PreferredSupplierId { get; set; }
    public string? PreferredSupplierCode { get; set; }
    public string? Remark { get; set; }

    public DateTime? RolledBackAt { get; set; }
    public string? RolledBackBy { get; set; }
    public string? RolledBackReason { get; set; }
}
