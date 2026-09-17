using SwiftExport.Core.Common;
using SwiftExport.Contracts.SysEnums;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.QualityControl;

/// <summary>保存售后问题与补发明细事实，不持有下游进度。</summary>
[Table("AfterSalesLine")]
public sealed partial class AfterSalesLine : BaseEntity
{
    /// <summary>所属售后单。</summary>
    public Guid HeaderId { get; set; }
    /// <summary>稳定行号。</summary>
    public int LineNumber { get; set; }
    /// <summary>原销售行。</summary>
    public Guid? OriginalSalesOrderLineId { get; set; }
    /// <summary>客户侧投诉单号，可空；开放单据确认后仍可补充，不影响实物要求。</summary>
    [Column(TypeName = "nvarchar(100)")]
    public string? CustomerComplaintNo { get; set; }
    /// <summary>责任供应商。</summary>
    public Guid? ResponsibleSupplierId { get; set; }
    /// <summary>问题成品身份。</summary>
    public Guid? IssueProductId { get; set; }
    /// <summary>问题物料编号。</summary>
    public string? IssueSkuSnapshot { get; set; }
    /// <summary>问题客户型号。</summary>
    public string? IssueCustomerSkuSnapshot { get; set; }
    /// <summary>问题AQ型号。</summary>
    public string? IssueLinkSkuSnapshot { get; set; }
    /// <summary>问题颜色。</summary>
    public Guid? IssueColorId { get; set; }
    /// <summary>问题颜色快照。</summary>
    public string? IssueColorSnapshot { get; set; }
    /// <summary>问题产品描述。</summary>
    public string? IssueProductDescription { get; set; }
    /// <summary>投诉描述。</summary>
    public string IssueDescription { get; set; } = string.Empty;
    /// <summary>品质分类。</summary>
    public Guid? QualityCategoryId { get; set; }
    /// <summary>原因分析。</summary>
    public string? CauseAnalysis { get; set; }
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
    /// <summary>单位，默认Pcs。</summary>
    public string UnitSnapshot { get; set; } = "Pcs";
    /// <summary>要求交期。</summary>
    public DateTime? RequiredDate { get; set; }
    /// <summary>人民币行金额，空未定零免费。</summary>
    public decimal? ServiceAmountCny { get; set; }
    /// <summary>退回品收到日期，不改变库存。</summary>
    public DateTime? ReturnedReceivedDate { get; set; }
    /// <summary>处理说明。</summary>
    public string? TreatmentDescription { get; set; }
    /// <summary>行备注。</summary>
    public string? Remark { get; set; }
}
