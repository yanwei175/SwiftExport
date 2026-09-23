using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.QualityControl;

/// <summary>保存售后单头和实物确认事实，不持有下游进度。</summary>
[Table("AfterSalesHeader")]
public sealed partial class AfterSalesHeader : BaseEntity
{
    /// <summary>售后单号。</summary>
    public string AfterSalesNo { get; set; } = string.Empty;
    /// <summary>客户身份。</summary>
    public Guid CustomerId { get; set; }
    /// <summary>客户编码快照。</summary>
    public string CustomerCodeSnapshot { get; set; } = string.Empty;
    /// <summary>客户名称快照。</summary>
    public string? CustomerNameSnapshot { get; set; }
    /// <summary>受理日期。</summary>
    public DateTime AcceptedDate { get; set; }
    /// <summary>负责人。</summary>
    public Guid? OwnerId { get; set; }
    /// <summary>生命周期。</summary>
    [Column(TypeName = "tinyint")]
    public DocumentStatusEnum DocumentStatus { get; set; }
    /// <summary>实物确认状态。</summary>
    [Column(TypeName = "tinyint")]
    public ConfirmationStatusEnum ConfirmationStatus { get; set; }
    /// <summary>当前确认时间。</summary>
    public DateTime? DemandConfirmedAt { get; set; }
    /// <summary>当前确认人。</summary>
    public string? DemandConfirmedBy { get; set; }
    /// <summary>预计完成日期。</summary>
    public DateTime? ExpectedCompletionDate { get; set; }


    /// <summary>结案说明。</summary>
    public string? Conclusion { get; set; }


    /// <summary>整单备注。</summary>
    public string? Remark { get; set; }
    /// <summary>原销售行。</summary>
    public Guid? OriginalSalesOrderLineId { get; set; }
    /// <summary>客户侧投诉单号，可空；开放单据确认后仍可补充，不影响实物要求。</summary>
    [Column(TypeName = "nvarchar(100)")]
    public string? CustomerComplaintNo { get; set; }
    /// <summary>本机投诉资料文件夹路径，仅保存文本，不参与来源需求或文件系统操作。</summary>
    [Column(TypeName = "nvarchar(1000)")]
    public string? LocalFileFolder { get; set; }
    /// <summary>责任供应商。</summary>
    public Guid? ResponsibleSupplierId { get; set; }
    /// <summary>问题成品身份。</summary>
    public Guid? IssueProductId { get; set; }
    /// <summary>问题物料编号。</summary>
    [Column(TypeName = "nvarchar(100)")]
    public string? IssueSkuSnapshot { get; set; }
    /// <summary>问题客户型号。</summary>
    [Column(TypeName = "nvarchar(100)")]
    public string? IssueCustomerSkuSnapshot { get; set; }
    /// <summary>问题AQ型号。</summary>
    [Column(TypeName = "nvarchar(100)")]
    public string? IssueLinkSkuSnapshot { get; set; }
    /// <summary>投诉描述。</summary>
    [Column(TypeName = "nvarchar(2000)")]
    public string IssueDescription { get; set; } = string.Empty;
    /// <summary>品质分类。</summary>
    public Guid? QualityCategoryId { get; set; }
    /// <summary>原因分析。</summary>
    [Column(TypeName = "nvarchar(2000)")]
    public string? CauseAnalysis { get; set; }
    /// <summary>要求交期。</summary>
    [Column(TypeName = "date")]
    public DateTime? RequiredDate { get; set; }
    /// <summary>整单人民币售后成本，空未定、零免费、正数付费，不乘补件数量。</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? ServiceAmountCny { get; set; }
    /// <summary>退回品收到日期，不改变库存。</summary>
    [Column(TypeName = "date")]
    public DateTime? ReturnedReceivedDate { get; set; }
    /// <summary>处理说明。</summary>
    [Column(TypeName = "nvarchar(2000)")]
    public string? TreatmentDescription { get; set; }

}
