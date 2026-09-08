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

    }
}

