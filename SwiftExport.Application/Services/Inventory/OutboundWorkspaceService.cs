using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Inventory;

namespace SwiftExport.Application.Services.Inventory;

public sealed class OutboundWorkspaceService(ServiceBaseContext context, IOutboundDocumentRepository documents) : IOutboundWorkspaceService
{
    private readonly ILogger<OutboundWorkspaceService> _logger = context.LoggerFactory.CreateLogger<OutboundWorkspaceService>();

    public Task<ServiceResult<PagedResponseBase<OutboundListItemDto>>> SearchAsync(OutboundSearchRequest request, CancellationToken cancellationToken = default)
        => ReadAsync(() => documents.SearchAsync(request ?? new OutboundSearchRequest(), cancellationToken), "查询出库工作台失败。");

    public Task<ServiceResult<OutboundDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
        => ReadAsync(async () =>
        {
            var header = await documents.GetHeaderAsync(uid, cancellationToken) ?? throw new InvalidOperationException("出库单不存在。");
            return new OutboundDetailDto { Header = header, Lines = (await documents.GetLinesAsync(uid, cancellationToken)).ToList(), Ledgers = (await documents.GetLedgersAsync(uid, cancellationToken)).ToList() };
        }, "加载出库单失败。");

    public Task<ServiceResult<OutboundTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default)
        => ReadAsync(async () =>
        {
            var detailResult = await GetDetailAsync(uid, cancellationToken);
            if (!detailResult.Success || detailResult.Data is null) throw new InvalidOperationException(detailResult.Message);
            var trace = new OutboundTraceDto { Document = detailResult.Data };
            trace.TraceItems.AddRange(detailResult.Data.Lines.Select(x => $"{x.SalesOrderNo} → Allocation {x.NetDemandAllocationId:N} → {x.Quantity:0.####} {x.Unit}"));
            return trace;
        }, "加载出库追溯失败。");

    private async Task<ServiceResult<T>> ReadAsync<T>(Func<Task<T>> action, string failure)
    {
        try { return ServiceResult<T>.Ok(await action()); }
        catch (InvalidOperationException ex) { return ServiceResult<T>.Fail(ex.Message); }
        catch (Exception ex) { _logger.LogError(ex, "{Failure}", failure); return ServiceResult<T>.Fail(failure); }
    }
}
