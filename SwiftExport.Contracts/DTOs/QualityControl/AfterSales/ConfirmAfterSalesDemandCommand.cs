using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>确认已保存正数量实物要求的幂等命令。</summary>
    public class ConfirmAfterSalesDemandCommand
    {
    /// <summary>幂等命令标识。</summary>
    public Guid CommandId { get; set; }
    /// <summary>头版本。</summary>
    public int Version { get; set; } = 1;

    }
}

