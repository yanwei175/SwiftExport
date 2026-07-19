using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

/// <summary>新入库明细，兼容映射 dbo.PurchaseReceiveLine。</summary>
[Table("PurchaseReceiveLine")]
public sealed class InboundDocumentLine : BaseEntity
{
    /// <summary>关联的入库单头唯一标识。</summary>
    public Guid InboundDocumentId { get; set; }
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
    /// <summary>本入库明细实际过账增加的产品数量。</summary>
    public decimal Quantity { get; set; }
    /// <summary>该实体保存的业务来源类型。</summary>
    public InboundSourceTypeEnum SourceType { get; set; }
    /// <summary>关联的业务来源记录唯一标识。</summary>
    public Guid SourceUid { get; set; }
    /// <summary>关联的需求执行追溯关系唯一标识。</summary>
    public Guid? DemandExecutionLinkId { get; set; }
    /// <summary>关联的净需求执行计划唯一标识。</summary>
    public Guid? NetDemandAllocationId { get; set; }
    /// <summary>关联的毛需求唯一标识。</summary>
    public Guid? GrossDemandId { get; set; }
    /// <summary>单据或业务事实形成时冻结的销售订单号历史快照，不随主数据更新。</summary>
    public string? SalesOrderNoSnapshot { get; set; }
    /// <summary>单据或业务事实形成时冻结的销售订单行号历史快照，不随主数据更新。</summary>
    public int? SalesOrderLineNumberSnapshot { get; set; }
    /// <summary>关联的原始业务明细唯一标识。</summary>
    public Guid? OriginalLineId { get; set; }
}
