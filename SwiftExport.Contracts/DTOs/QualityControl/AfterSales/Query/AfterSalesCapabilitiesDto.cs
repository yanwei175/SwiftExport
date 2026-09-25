using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>售后当前状态允许的动作；下游资格仍在执行命令时复核。</summary>
    public class AfterSalesCapabilitiesDto
    {
    /// <summary>当前动作准入，不代替服务端复核。</summary>
    public bool CanSave { get; set; }
    /// <summary>当前动作准入，不代替服务端复核。</summary>
    public bool CanConfirm { get; set; }
    /// <summary>当前动作准入，不代替服务端复核。</summary>
    public bool CanCancelConfirmation { get; set; }
    /// <summary>当前动作准入，不代替服务端复核。</summary>
    public bool CanClose { get; set; }
    /// <summary>当前动作准入，不代替服务端复核。</summary>
    public bool CanReopen { get; set; }
    /// <summary>当前动作准入，不代替服务端复核。</summary>
    public bool CanVoid { get; set; }
    /// <summary>当前动作准入，不代替服务端复核。</summary>
    public bool CanEditFacts { get; set; }

    }
}

