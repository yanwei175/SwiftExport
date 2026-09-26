namespace SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;

/// <summary>来源领域提交的一项独立实物需求事实。</summary>
public sealed class SourceDemandReceptionItem
{
    public Guid SourceLineId { get; set; }
    public int? SourceLineNumber { get; set; }
    public Guid? ProductId { get; set; }
    public bool IsNonStandard { get; set; }
    public string SkuSnapshot { get; set; } = string.Empty;
    public string? LinkSkuSnapshot { get; set; }
    public string? CustomerSkuSnapshot { get; set; }
    public string? DescriptionSnapshot { get; set; }
    public string? ColorSnapshot { get; set; }
    public string? SpecificationSnapshot { get; set; }
    public string? ImagePathSnapshot { get; set; }
    public string? UnitSnapshot { get; set; }
    public decimal DemandQty { get; set; }
    public DateTime? RequiredDate { get; set; }
    public string? Remark { get; set; }
}
