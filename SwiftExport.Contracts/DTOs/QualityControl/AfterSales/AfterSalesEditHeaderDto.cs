using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>售后单头可编辑事实，不开放单号、状态与审计字段。</summary>
    public class AfterSalesEditHeaderDto
    {
    /// <summary>客户身份。</summary>
    public Guid CustomerId { get; set; }
    /// <summary>受理日期。</summary>
    public DateTime AcceptedDate { get; set; }
    /// <summary>负责人。</summary>
    public Guid? OwnerId { get; set; }
    /// <summary>预计完成日期。</summary>
    public DateTime? ExpectedCompletionDate { get; set; }
    /// <summary>整单备注。</summary>
    public string Remark { get; set; } = string.Empty;

    /// <summary>原销售行。</summary>
    public Guid? OriginalSalesOrderLineId { get; set; }
    /// <summary>客户侧投诉单号，可空；开放单据确认后允许补填或修改。</summary>
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

    /// <summary>用户填写的本机投诉资料文件夹路径，仅保存字符串。</summary>
    public string LocalFileFolder { get; set; }

    }
}

