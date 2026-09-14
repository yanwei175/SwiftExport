namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

public sealed class NetDemandManagerOpenRequest
{
    public Guid? NetDemandId { get; init; }
    public Guid? GrossDemandId { get; init; }
    public Guid? SourceUid { get; init; }
    public string? SourceNo { get; init; }
}
