namespace SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;

/// <summary>来源需求接收结果，区分新增、更新和无变化的需求行。</summary>
public sealed class SourceDemandReceptionResult
{
    public int SourceVersion { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int TotalCount => CreatedCount + UpdatedCount + UnchangedCount;
}
