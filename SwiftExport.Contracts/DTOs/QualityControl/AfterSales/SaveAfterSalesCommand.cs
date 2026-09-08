using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>以完整单头和显式行变更集合保存售后事实；保存不接收需求。</summary>
    public class SaveAfterSalesCommand
    {
    /// <summary>幂等命令标识。</summary>
    public Guid CommandId { get; set; }
    /// <summary>单据稳定身份。</summary>
    public Guid Uid { get; set; }
    /// <summary>头版本。</summary>
    public int Version { get; set; } = 1;
    public AfterSalesEditHeaderDto Header { get; set; } = new AfterSalesEditHeaderDto();
    public List<AfterSalesEditLineDto> AddedLines { get; set; } = new List<AfterSalesEditLineDto>();
    public List<AfterSalesEditLineDto> UpdatedLines { get; set; } = new List<AfterSalesEditLineDto>();
    public List<AfterSalesDeletedLineDto> DeletedLines { get; set; } = new List<AfterSalesDeletedLineDto>();
    }
}

