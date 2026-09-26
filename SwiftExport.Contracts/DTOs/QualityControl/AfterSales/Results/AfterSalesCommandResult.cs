using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>售后写命令的权威结果，包含最新头版本、状态及幂等标记。</summary>
    public class AfterSalesCommandResult
    {
    /// <summary>单据身份。</summary>
    public Guid Uid { get; set; }
    /// <summary>售后单号。</summary>
    public string AfterSalesNo { get; set; } = string.Empty;
    /// <summary>最新版本。</summary>
    public int Version { get; set; } = 1;
    /// <summary>幂等重放。</summary>
    public bool AlreadyApplied { get; set; }
    /// <summary>生命周期。</summary>
    public DocumentStatusEnum DocumentStatus { get; set; }
    /// <summary>实物确认状态。</summary>
    public ConfirmationStatusEnum ConfirmationStatus { get; set; }
    public AfterSalesCapabilitiesDto Capabilities { get; set; } = new AfterSalesCapabilitiesDto();
    }
}

