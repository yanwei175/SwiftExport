using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning.Sources.Delivery;

/// <summary>保留旧来源需求交付只读 API 的兼容边界，实际查询统一转发给 Shipping 交付服务。</summary>
public interface ISourceDemandDeliveryService
{
    /// <summary>查询销售与投诉补货来源的当前可交付要求，不依赖物料需求或 Allocation。</summary>
    Task<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>> SearchAsync(
        SourceDemandDeliverySearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>读取单项来源需求的交付上下文和库存动作阻断原因。</summary>
    Task<ServiceResult<SourceDemandDeliveryItemDto>> GetAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken = default);

}
