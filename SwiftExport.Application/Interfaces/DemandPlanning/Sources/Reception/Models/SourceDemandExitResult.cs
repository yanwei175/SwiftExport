namespace SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;

/// <summary>来源确认退出结果，保留物理删除和作废留痕两种既有路径。</summary>
public sealed class SourceDemandExitResult
{
    public int HardDeletedCount { get; set; }
    public int VoidedCount { get; set; }
}
