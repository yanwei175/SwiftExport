using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

/// <summary>出库单明细实体，保存产品出库数量及其装箱和需求来源。</summary>
[Table("OutboundDocumentLine")]
public sealed class OutboundDocumentLine : BaseEntity
{
    /// <summary>关联的出库单头唯一标识。</summary>
    public Guid OutboundDocumentId { get; set; }
    /// <summary>该实体保存的单据行号。</summary>
    public int LineNumber { get; set; }
    /// <summary>关联的产品主数据唯一标识。</summary>
    public Guid ProductId { get; set; }
    /// <summary>单据或业务事实形成时冻结的产品编码历史快照，不随主数据更新。</summary>
    public string SkuSnapshot { get; set; } = string.Empty;
    /// <summary>单据或业务事实形成时冻结的产品描述历史快照，不随主数据更新。</summary>
    public string? DescriptionSnapshot { get; set; }
    /// <summary>单据或业务事实形成时冻结的计量单位历史快照，不随主数据更新。</summary>
    public string? UnitSnapshot { get; set; }
    /// <summary>本出库明细实际过账扣减的产品数量。</summary>
    public decimal Quantity { get; set; }
    /// <summary>关联的装箱单来源贡献行唯一标识。</summary>
    public Guid PackingListContributionId { get; set; }
    /// <summary>关联的净需求执行计划唯一标识。</summary>
    /// <summary>可选的计划分配追溯标识；正式客户出库以 GrossDemandId 为交付锚点。</summary>
    public Guid? NetDemandAllocationId { get; set; }
    /// <summary>关联的毛需求唯一标识。</summary>
    public Guid? GrossDemandId { get; set; }
    /// <summary>单据或业务事实形成时冻结的销售订单号历史快照，不随主数据更新。</summary>
    public string? SalesOrderNoSnapshot { get; set; }
    /// <summary>业务命令明细唯一标识，用于保障批量命令明细幂等执行。</summary>
    public Guid CommandItemId { get; set; }
    /// <summary>关联的原始业务明细唯一标识。</summary>
    public Guid? OriginalLineId { get; set; }
}
