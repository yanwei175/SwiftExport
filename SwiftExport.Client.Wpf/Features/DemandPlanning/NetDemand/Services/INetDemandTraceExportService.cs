using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Services;

public interface INetDemandTraceExportService
{
    Task<string?> ExportSourceAsync(
        SourcePurchaseTraceDto trace,
        CancellationToken cancellationToken = default);

    Task<string?> ExportAsync(
        NetDemandListItemDto summary,
        IReadOnlyList<NetDemandAllocationDto> allocations,
        IReadOnlyList<NetDemandExecutionLinkDto> executionLinks,
        CancellationToken cancellationToken = default);
}
