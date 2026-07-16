using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.DemandPlanning;

/// <summary>
/// 原始业务需求。兼容期继续落在 IndependentDemand 物理表。
/// </summary>
[Table("IndependentDemand")]
public sealed class GrossDemand : BaseEntity
{
    public GrossDemandLifecycleStatusEnum LifecycleStatus { get; set; } = GrossDemandLifecycleStatusEnum.Active;
    public GrossDemandDecompositionStatusEnum DecompositionStatus { get; set; } = GrossDemandDecompositionStatusEnum.Pending;
    public int DecompositionRevision { get; set; }

    public string SourceType { get; set; } = string.Empty;
    public Guid SourceUid { get; set; }
    public string SourceNo { get; set; } = string.Empty;
    public Guid SourceLineId { get; set; }
    public int? SourceLineNumber { get; set; }

    public Guid? ProductId { get; set; }
    public bool IsNonStandard { get; set; }
    public string SkuSnapshot { get; set; } = string.Empty;
    public string? LinkSkuSnapshot { get; set; }
    public string? CustomerSkuSnapshot { get; set; }
    public string? DescriptionSnapshot { get; set; }
    public string? ColorSnapshot { get; set; }
    public string? SpecificationSnapshot { get; set; }
    public string? ImagePathSnapshot { get; set; }

    public decimal DemandQty { get; set; }
    public DateTime? RequiredDate { get; set; }
    public string? Remark { get; set; }

    public DateTime? DecomposedAt { get; set; }
    public string? DecomposedBy { get; set; }
    public DateTime? LifecycleChangedAt { get; set; }
    public string? LifecycleChangedBy { get; set; }
    public string? LifecycleChangedReason { get; set; }
}
