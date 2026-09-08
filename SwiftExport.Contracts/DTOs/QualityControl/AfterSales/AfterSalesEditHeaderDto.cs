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

    }
}

