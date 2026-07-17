using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Core.Interfaces.Inventory;

namespace SwiftExport.Infrastructure.Repositories.Inventory;

public sealed class InboundQueryRepository(IInboundDocumentRepository documents) : IInboundQueryRepository
{
    public async Task<InboundTraceDto?> GetTraceAsync(Guid inboundDocumentId, CancellationToken cancellationToken = default)
    {
        var header = await documents.GetHeaderAsync(inboundDocumentId, cancellationToken);
        if (header is null) return null;
        var lines = (await documents.GetLinesAsync(inboundDocumentId, cancellationToken)).ToList();
        var ledgers = (await documents.GetLedgersAsync(inboundDocumentId, cancellationToken)).ToList();
        return new InboundTraceDto { Document = new InboundDetailDto { Header = header, Lines = lines, Ledgers = ledgers }, TraceItems = lines.Select(x => $"{x.SalesOrderNo} -> {x.PurchaseOrderNo} -> {header.DocumentNo} -> {x.Sku}").ToList() };
    }
}
