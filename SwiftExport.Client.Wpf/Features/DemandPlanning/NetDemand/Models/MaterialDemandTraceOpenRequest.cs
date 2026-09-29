namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

/// <summary>以稳定物料行身份打开只读追踪页。</summary>
public sealed class MaterialDemandTraceOpenRequest
{
    /// <summary>要追踪的物料需求 Uid。</summary>
    public Guid NetDemandId { get; init; }
}
