using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Shipping;

/// <summary>装箱明细，以成品需求锚定交付要求，保存产品及实际包装快照，不承担采购数量分摊。</summary>
[Table("packing_list_line")]
public sealed partial class PackingListLine : BaseEntity
{
    /// <summary>所属装箱单头 Uid，逻辑关联。</summary>
    public Guid PackingListHeadId { get; set; }
    /// <summary>单据内行号，同来源允许多行。</summary>
   public int LineNumber { get; set; }
    /// <summary>同单据内一只拼箱的稳定标识；为空表示普通包装行，不关联独立组表。</summary>
    public Guid? PackingGroupId { get; set; }
    /// <summary>交付来源锚点；仅未关联草稿允许为空，不表示采购或物料分摊。</summary>
    public Guid? GrossDemandId { get; set; }
    /// <summary>标准行产品标识；非标行为空。</summary>
    public Guid? ProductId { get; set; }
    /// <summary>是否为不产生库存事实的非标来源快照。</summary>
    public bool IsNonStandardSnapshot { get; set; }
    /// <summary>来源类型快照，历史展示不回查当前来源。</summary>
    public string SourceTypeSnapshot { get; set; } = string.Empty;
    /// <summary>来源单号快照，历史展示不回查当前来源。</summary>
    public string SourceDocumentNoSnapshot { get; set; } = string.Empty;
    /// <summary>来源行号快照。</summary>
    public int? SourceLineNumberSnapshot { get; set; }
    /// <summary>产品编码快照。</summary>
    public string SkuSnapshot { get; set; } = string.Empty;
    /// <summary>关联型号快照。</summary>
    public string? LinkSkuSnapshot { get; set; }
    /// <summary>客户型号快照。</summary>
    public string? CustomerSkuSnapshot { get; set; }
    /// <summary>英文描述快照。</summary>
    public string? DescriptionEnSnapshot { get; set; }
    /// <summary>英文颜色名称快照。</summary>
    public string? ColorNameEnSnapshot { get; set; }
    /// <summary>库存及交付数量的计量单位快照。</summary>
    public string? UnitSnapshot { get; set; }
    /// <summary>本行计划交付数量，草稿不形成正式交付事实。</summary>
    public decimal Qty { get; set; }
    /// <summary>每包数量；未知为空，已知时须大于零。</summary>
    public decimal? PcsPerPackage { get; set; }
    /// <summary>数量除以每箱数量计算的箱数，四位小数，不向上取整。</summary>
    public decimal? PackageCount { get; set; }
    /// <summary>每包净重。未知为空。</summary>
    public decimal? NwPerPackage { get; set; }
    /// <summary>每包毛重。未知为空。</summary>
    public decimal? GwPerPackage { get; set; }
    /// <summary>每包体积。未知为空。</summary>
    public decimal? CbmPerPackage { get; set; }
    /// <summary>实际包装数乘单包指标的结果快照；保存入口统一重算，四位小数 AwayFromZero，未知为空。</summary>
    public decimal? SubTotalNw { get; set; }
    /// <summary>实际包装数乘单包指标的结果快照；保存入口统一重算，四位小数 AwayFromZero，未知为空。</summary>
    public decimal? SubTotalGw { get; set; }
    /// <summary>实际包装数乘单包指标的结果快照；保存入口统一重算，四位小数 AwayFromZero，未知为空。</summary>
    public decimal? SubTotalCbm { get; set; }
    /// <summary>客户采购编号文本快照，不作为关系主键。</summary>
    public string? CustomerPoNo { get; set; }
    /// <summary>行上手工填写的发票号文本，不关联财务发票。</summary>
    public string? InvoiceNo { get; set; }
    /// <summary>本行箱唛。</summary>
    public string? Marks { get; set; }
    /// <summary>本行备注。</summary>
    public string? Note { get; set; }

}
