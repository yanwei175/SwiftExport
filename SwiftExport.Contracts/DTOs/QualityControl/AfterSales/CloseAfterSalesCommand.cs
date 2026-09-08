using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>带原因请求结案，由来源领域最终复核剩余要求。</summary>
    public class CloseAfterSalesCommand
    {
    /// <summary>幂等命令标识。</summary>
    public Guid CommandId { get; set; }
    /// <summary>头版本。</summary>
    public int Version { get; set; } = 1;
    /// <summary>操作原因或结案说明。</summary>
    public string Reason { get; set; } = string.Empty;

    }
}

