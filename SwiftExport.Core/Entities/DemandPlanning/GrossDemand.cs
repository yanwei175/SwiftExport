using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.DemandPlanning;

/// <summary>
/// 原始业务需求。
/// </summary>
[Table("gross_demand")]
public sealed partial class GrossDemand : BaseEntity
{
    /// <summary>毛需求生命周期；关闭后不能继续拆解。</summary>
    public GrossDemandLifecycleStatusEnum LifecycleStatus { get; set; } = GrossDemandLifecycleStatusEnum.Active;
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
    /// <summary>来源单据头在本需求被接收时的事实版本，用于拒绝旧版本覆盖。</summary>
    public int? SourceVersion { get; set; }
    /// <summary>来源需求实物内容的稳定指纹；历史数据无法确认时为空。</summary>
    public string? DemandBasisFingerprint { get; set; }
    /// <summary>来源需求对应的交付客户；手工需求及历史数据可以为空。</summary>
    public Guid? CustomerId { get; set; }

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
    /// <summary>来源需求接收时的产品单位快照；未关联产品的非标需求可以为空。</summary>
    public string? UnitSnapshot { get; set; }

    /// <summary>原始业务需求数量，是拆解数量计算的起点。</summary>
    public decimal DemandQty { get; set; }
    /// <summary>来源业务要求的交期。</summary>
    public DateTime? RequiredDate { get; set; }
    /// <summary>来源需求备注快照。</summary>
    public string? Remark { get; set; }

    /// <summary>最近一次生命周期变更时间。</summary>
    public DateTime? LifecycleChangedAt { get; set; }
    /// <summary>最近一次生命周期变更操作人。</summary>
    public string? LifecycleChangedBy { get; set; }
    /// <summary>关闭或恢复等生命周期变更原因。</summary>
    public string? LifecycleChangedReason { get; set; }
}
