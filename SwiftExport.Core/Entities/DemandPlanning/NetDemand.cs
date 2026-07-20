using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.DemandPlanning;

/// <summary>
/// 毛需求一次拆解版本中的计划执行行。
/// </summary>
[Table("NetDemand")]
public sealed class NetDemand : BaseEntity
{
    /// <summary>所属毛需求 Uid。</summary>
    public Guid GrossDemandId { get; set; }
    /// <summary>生成本行的毛需求拆解修订号。</summary>
    public int DecompositionRevision { get; set; }
    /// <summary>同一毛需求、同一拆解版本内的稳定行号。</summary>
    public int LineNumber { get; set; }
    /// <summary>净需求生命周期；回退拆解时旧行会被取消而不是删除历史。</summary>
    public NetDemandLifecycleStatusEnum LifecycleStatus { get; set; } = NetDemandLifecycleStatusEnum.Active;

    /// <summary>父级净需求 Uid；根产品为空，客供子件指向父件。</summary>
    public Guid? ParentNetDemandId { get; set; }
    /// <summary>本棵拆解树的根净需求 Uid；根行指向自身。</summary>
    public Guid RootNetDemandId { get; set; }
    /// <summary>送达目标净需求 Uid；客供件通常指向父件净需求。</summary>
    public Guid? DeliveryTargetNetDemandId { get; set; }
    /// <summary>送达规则；标准交付或送往父件的执行供应商。</summary>
    public DemandDeliveryModeEnum DeliveryMode { get; set; } = DemandDeliveryModeEnum.Standard;

    /// <summary>关联产品 Uid；非标净需求可以为空。</summary>
    public Guid? ProductId { get; set; }
    /// <summary>生成本净需求的 BOM 行 Uid；根行和非标行为空。</summary>
    public Guid? BomLineId { get; set; }
    /// <summary>拆解时采用的 BOM 版本快照。</summary>
    public int? BomVersion { get; set; }
    /// <summary>从根产品到当前物料的 BOM 路径快照。</summary>
    public string? BomPath { get; set; }
    /// <summary>拆解时的物料类型快照，例如成品、配件或客供件。</summary>
    public string? MaterialTypeSnapshot { get; set; }
    /// <summary>拆解时的供货方式快照，例如采购、生产或外协。</summary>
    public string SupplyTypeSnapshot { get; set; } = string.Empty;
    /// <summary>拆解时的 SKU 快照。</summary>
    public string SkuSnapshot { get; set; } = string.Empty;
    /// <summary>父级 SKU 快照；根行通常为空字符串。</summary>
    public string? ParentSkuSnapshot { get; set; }
    /// <summary>客户型号快照。</summary>
    public string? CustomerSkuSnapshot { get; set; }
    /// <summary>产品描述快照。</summary>
    public string? DescriptionSnapshot { get; set; }
    /// <summary>颜色快照。</summary>
    public string? ColorSnapshot { get; set; }
    /// <summary>规格特征快照。</summary>
    public string? SpecificationSnapshot { get; set; }

    /// <summary>BOM 展开后得到的原始需求数量。</summary>
    public decimal GrossQty { get; set; }
    /// <summary>当前净需求数量；现阶段未扣库存时通常等于 GrossQty。</summary>
    public decimal NetQty { get; set; }
    /// <summary>从毛需求继承的原始业务交期，只读保留。</summary>
    public DateTime? RequiredDate { get; set; }
    /// <summary>拆解产生或人工维护的净需求备注。</summary>
    public string? Remark { get; set; }

    /// <summary>拆解回退时间。</summary>
    public DateTime? RolledBackAt { get; set; }
    /// <summary>拆解回退操作人。</summary>
    public string? RolledBackBy { get; set; }
    /// <summary>拆解回退原因。</summary>
    public string? RolledBackReason { get; set; }
}
