using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>售后管理列表单头摘要，已知金额与未定费用分别表达。</summary>
    public class AfterSalesListItemDto
    {
    /// <summary>稳定身份。</summary>
    public Guid Uid { get; set; }
    /// <summary>并发版本。</summary>
    public int Version { get; set; } = 1;
    /// <summary>单头客户投诉单号，允许为空及重复。</summary>
    public string CustomerComplaintNo { get; set; }
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
    /// <summary>预计完成日期。</summary>
    public DateTime? ExpectedCompletionDate { get; set; }
    /// <summary>负责人名称。</summary>
    public string OwnerName { get; set; } = string.Empty;
    /// <summary>问题型号摘要。</summary>
    public string IssueModels { get; set; } = string.Empty;
    /// <summary>责任方摘要。</summary>
    public string ResponsibleSuppliers { get; set; } = string.Empty;
    /// <summary>单头已知人民币成本；未定时投影为零，须结合未定标记展示。</summary>
    public decimal KnownAmountCny { get; set; }
    /// <summary>单头人民币成本是否未定，不再表示多行汇总。</summary>
    public bool HasUndeterminedAmount { get; set; }

    }
}
