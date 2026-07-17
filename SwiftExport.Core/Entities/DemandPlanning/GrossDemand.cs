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
    /// <summary>毛需求生命周期；关闭后不能继续拆解。</summary>
    public GrossDemandLifecycleStatusEnum LifecycleStatus { get; set; } = GrossDemandLifecycleStatusEnum.Active;
    /// <summary>当前是否已经成功拆解为净需求。</summary>
    public GrossDemandDecompositionStatusEnum DecompositionStatus { get; set; } = GrossDemandDecompositionStatusEnum.Pending;
    /// <summary>拆解修订号；每次成功拆解递增，用于识别同一毛需求的拆解版本。</summary>
    public int DecompositionRevision { get; set; }

    /// <summary>来源单据类型，例如销售订单、手工需求或投诉补货。</summary>
    public string SourceType { get; set; } = string.Empty;
    /// <summary>来源单据头或手工需求自身的 Uid。</summary>
    public Guid SourceUid { get; set; }
    /// <summary>供用户查账的来源单号快照。</summary>
    public string SourceNo { get; set; } = string.Empty;
    /// <summary>来源明细行 Uid；手工需求通常与自身 Uid 相同。</summary>
    public Guid SourceLineId { get; set; }
    /// <summary>来源明细行号快照。</summary>
    public int? SourceLineNumber { get; set; }

    /// <summary>标准产品 Uid；非标需求可以为空。</summary>
    public Guid? ProductId { get; set; }
    /// <summary>是否为不依赖产品主数据和 BOM 的非标需求。</summary>
    public bool IsNonStandard { get; set; }
    /// <summary>毛需求成立时的 SKU 快照。</summary>
    public string SkuSnapshot { get; set; } = string.Empty;
    /// <summary>关联 SKU 快照，用于产品或客户型号追溯。</summary>
    public string? LinkSkuSnapshot { get; set; }
    /// <summary>客户型号快照。</summary>
    public string? CustomerSkuSnapshot { get; set; }
    /// <summary>产品描述快照。</summary>
    public string? DescriptionSnapshot { get; set; }
    /// <summary>颜色快照。</summary>
    public string? ColorSnapshot { get; set; }
    /// <summary>规格特征快照。</summary>
    public string? SpecificationSnapshot { get; set; }
    /// <summary>产品图片路径快照，仅用于追溯和展示。</summary>
    public string? ImagePathSnapshot { get; set; }

    /// <summary>原始业务需求数量，是拆解数量计算的起点。</summary>
    public decimal DemandQty { get; set; }
    /// <summary>来源业务要求的交期。</summary>
    public DateTime? RequiredDate { get; set; }
    /// <summary>来源需求备注快照。</summary>
    public string? Remark { get; set; }

    /// <summary>最近一次成功拆解时间。</summary>
    public DateTime? DecomposedAt { get; set; }
    /// <summary>最近一次成功拆解操作人。</summary>
    public string? DecomposedBy { get; set; }
    /// <summary>最近一次生命周期变更时间。</summary>
    public DateTime? LifecycleChangedAt { get; set; }
    /// <summary>最近一次生命周期变更操作人。</summary>
    public string? LifecycleChangedBy { get; set; }
    /// <summary>关闭或恢复等生命周期变更原因。</summary>
    public string? LifecycleChangedReason { get; set; }
}
