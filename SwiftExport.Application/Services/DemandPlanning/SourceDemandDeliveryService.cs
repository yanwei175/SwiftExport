using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>兼容旧来源需求交付查询入口，单向转发到 Shipping 的唯一交付实现。</summary>
public sealed class SourceDemandDeliveryService(ISourceDeliveryService delivery) : ISourceDemandDeliveryService
{
    /// <inheritdoc />
    public Task<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>> SearchAsync(
        SourceDemandDeliverySearchRequest request,
        CancellationToken cancellationToken = default)
        => delivery.SearchAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<SourceDemandDeliveryItemDto>> GetAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken = default)
        => delivery.GetAsync(grossDemandId, cancellationToken);
}
