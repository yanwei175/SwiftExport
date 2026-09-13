using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

/// <summary>入库单明细，保存产品、数量和可追溯来源。</summary>
[Table("InboundDocumentLine")]
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
    /// <summary>实际供货单据行标识；由单头入库类型解释，采购入库指向采购行，不指向需求、Link 或装箱贡献。</summary>
    public Guid SourceLineId { get; set; }
    /// <summary>关联的原始业务明细唯一标识。</summary>
    public Guid? OriginalLineId { get; set; }
}
