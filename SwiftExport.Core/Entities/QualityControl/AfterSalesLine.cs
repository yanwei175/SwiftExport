using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.QualityControl;

/// <summary>保存实际补件明细事实，不持有下游进度。</summary>
[Table("after_sales_line")]
public sealed partial class AfterSalesLine : BaseEntity
{
    /// <summary>所属售后单。</summary>
    public Guid HeaderId { get; set; }
    /// <summary>稳定行号。</summary>
    public int LineNumber { get; set; }






    /// <summary>实际补发物料。</summary>
    public Guid? ProductId { get; set; }
    /// <summary>实际补发非标标记。</summary>
    public bool IsNonStandard { get; set; }
    /// <summary>补发SKU。</summary>
    public string? SkuSnapshot { get; set; }
    /// <summary>补发客户型号。</summary>
    public string? CustomerSkuSnapshot { get; set; }
    /// <summary>补发AQ型号。</summary>
    public string? LinkSkuSnapshot { get; set; }
    /// <summary>补发描述。</summary>
    public string? DescriptionSnapshot { get; set; }
    /// <summary>补发颜色。</summary>
    public Guid? ColorId { get; set; }
    /// <summary>补发颜色快照。</summary>
    public string? ColorSnapshot { get; set; }
    /// <summary>补发规格。</summary>
    public string? SpecificationSnapshot { get; set; }
    /// <summary>补发数量，空或零不创建来源需求。</summary>
    public decimal? Quantity { get; set; }
    /// <summary>补件单位快照；缺失保持未知，确认实物需求前必须明确。</summary>
    public string UnitSnapshot { get; set; } = string.Empty;
    /// <summary>行备注。</summary>
    public string? Remark { get; set; }
}
