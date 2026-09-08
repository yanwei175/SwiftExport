using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>售后安全候选投影，产品候选额外携带选择后的快照资料。</summary>
    public class AfterSalesLookupDto
    {
    /// <summary>候选身份。</summary>
    public Guid Uid { get; set; }
    /// <summary>候选业务编码。</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>候选显示名称。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>产品候选客户型号。</summary>
    public string CustomerSku { get; set; } = string.Empty;
    /// <summary>产品候选AQ型号。</summary>
    public string LinkSku { get; set; } = string.Empty;
    /// <summary>产品单位。</summary>
    public string Unit { get; set; } = string.Empty;
    /// <summary>产品颜色。</summary>
    public Guid? ColorId { get; set; }
    /// <summary>颜色名称。</summary>
    public string ColorName { get; set; } = string.Empty;
    /// <summary>产品规格。</summary>
    public string Specification { get; set; } = string.Empty;

    }
}

