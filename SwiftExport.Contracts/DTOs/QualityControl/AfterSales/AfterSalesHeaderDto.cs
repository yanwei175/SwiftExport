using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>售后单头读取快照，包含生命周期和实物确认信息。</summary>
    public class AfterSalesHeaderDto
    {
    /// <summary>稳定身份。</summary>
    public Guid Uid { get; set; }
    /// <summary>并发版本。</summary>
    public int Version { get; set; } = 1;
    /// <summary>售后单号。</summary>
    public string AfterSalesNo { get; set; } = string.Empty;
    /// <summary>客户身份。</summary>
    public Guid CustomerId { get; set; }
    /// <summary>客户编码快照。</summary>
    public string CustomerCodeSnapshot { get; set; } = string.Empty;
    /// <summary>客户名称快照。</summary>
    public string CustomerNameSnapshot { get; set; } = string.Empty;
    /// <summary>受理日期。</summary>
    public DateTime AcceptedDate { get; set; }
    /// <summary>负责人。</summary>
    public Guid? OwnerId { get; set; }
    /// <summary>生命周期。</summary>
    public DocumentStatusEnum DocumentStatus { get; set; }
    /// <summary>实物确认状态。</summary>
    public ConfirmationStatusEnum ConfirmationStatus { get; set; }
    /// <summary>当前确认时间。</summary>
    public DateTime? DemandConfirmedAt { get; set; }
    /// <summary>当前确认人。</summary>
    public string DemandConfirmedBy { get; set; } = string.Empty;
    /// <summary>预计完成日期。</summary>
    public DateTime? ExpectedCompletionDate { get; set; }
    /// <summary>结案说明。</summary>
    public string Conclusion { get; set; } = string.Empty;
    /// <summary>整单备注。</summary>
    public string Remark { get; set; } = string.Empty;

    /// <summary>原销售行。</summary>
    public Guid? OriginalSalesOrderLineId { get; set; }
    /// <summary>客户侧投诉单号，可空，最多100字。</summary>
    public string CustomerComplaintNo { get; set; }
    /// <summary>责任供应商。</summary>
    public Guid? ResponsibleSupplierId { get; set; }
    /// <summary>问题成品身份。</summary>
    public Guid? IssueProductId { get; set; }
    /// <summary>问题物料编号。</summary>
    public string IssueSkuSnapshot { get; set; } = string.Empty;
    /// <summary>问题客户型号。</summary>
    public string IssueCustomerSkuSnapshot { get; set; } = string.Empty;
    /// <summary>问题AQ型号。</summary>
    public string IssueLinkSkuSnapshot { get; set; } = string.Empty;
    /// <summary>投诉描述。</summary>
    public string IssueDescription { get; set; } = string.Empty;
    /// <summary>品质分类。</summary>
    public Guid? QualityCategoryId { get; set; }
    /// <summary>原因分析。</summary>
    public string CauseAnalysis { get; set; } = string.Empty;
    /// <summary>要求交期。</summary>
    public DateTime? RequiredDate { get; set; }
    /// <summary>整单人民币售后成本，空未定、零免费、正数付费，不乘补件数量。</summary>
    public decimal? ServiceAmountCny { get; set; }
    /// <summary>退回品收到日期，不改变库存。</summary>
    public DateTime? ReturnedReceivedDate { get; set; }
    /// <summary>处理说明。</summary>
    public string TreatmentDescription { get; set; } = string.Empty;

    /// <summary>产品控件提供的图片路径，仅供显示，不作为售后存储事实。</summary>
    public string ImagePath { get; set; }

    /// <summary>用户填写的本机投诉资料文件夹路径，仅保存字符串。</summary>
    public string LocalFileFolder { get; set; }

    }
}

