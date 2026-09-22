using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>实际补件可编辑事实与并发身份。</summary>
    public class AfterSalesEditLineDto
    {
    /// <summary>稳定身份。</summary>
    public Guid Uid { get; set; }
    /// <summary>并发版本。</summary>
    public int Version { get; set; } = 1;
    /// <summary>实际补发物料。</summary>
    public Guid? ProductId { get; set; }
    /// <summary>实际补发非标标记。</summary>
    public bool IsNonStandard { get; set; }
    /// <summary>补发SKU。</summary>
    public string SkuSnapshot { get; set; } = string.Empty;
    /// <summary>补发客户型号。</summary>
    public string CustomerSkuSnapshot { get; set; } = string.Empty;
    /// <summary>补发AQ型号。</summary>
    public string LinkSkuSnapshot { get; set; } = string.Empty;
    /// <summary>补发描述。</summary>
    public string DescriptionSnapshot { get; set; } = string.Empty;
    /// <summary>补发颜色。</summary>
    public Guid? ColorId { get; set; }
    /// <summary>补发颜色快照。</summary>
    public string ColorSnapshot { get; set; } = string.Empty;
    /// <summary>补发规格。</summary>
    public string SpecificationSnapshot { get; set; } = string.Empty;
    /// <summary>补发数量，空或零不创建来源需求。</summary>
    public decimal? Quantity { get; set; }
    /// <summary>单位，默认Pcs。</summary>
    public string UnitSnapshot { get; set; } = "Pcs";
    /// <summary>行备注。</summary>
    public string Remark { get; set; } = string.Empty;

    }
}
