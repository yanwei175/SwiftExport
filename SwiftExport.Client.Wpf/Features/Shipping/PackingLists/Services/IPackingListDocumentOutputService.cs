using SwiftExport.Contracts.DTOs.Shipping.PackingLists;

namespace SwiftExportClient.Features.Shipping.PackingLists.Services;

public interface IPackingListDocumentOutputService
{
    Task<string?> ExportAsync(PackingListExportDto data, CancellationToken cancellationToken = default);
    Task PrintAsync(PackingListExportDto data, CancellationToken cancellationToken = default);
}
