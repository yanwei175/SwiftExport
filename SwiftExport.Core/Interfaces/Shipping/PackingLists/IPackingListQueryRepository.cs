using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Shipping.PackingLists;

/// <summary>装箱单管理、详情与来源候选的只读持久化边界。</summary>
public interface IPackingListQueryRepository
{
    /// <summary>检查装箱单结构是否已就绪，不修改数据库。</summary>
    Task<bool> IsSchemaReadyAsync(CancellationToken token = default);
    /// <summary>分页查询管理摘要。</summary>
    Task<PagedResponseBase<PackingListManagerListItemDto>> SearchAsync(PackingListManagerSearchRequest request, CancellationToken token = default);
    /// <summary>读取单据完整详情。</summary>
    Task<PackingListDetailDto?> GetDetailAsync(Guid uid, CancellationToken token = default);
    /// <summary>分页读取符合当前交付资格的来源。</summary>
    Task<PagedResponseBase<SourceDemandDeliveryItemDto>> SearchSourcesAsync(PackingListSourceSearchRequest request, CancellationToken token = default);
    /// <summary>读取来源专属包装及客户参考号补充信息。</summary>
    Task<IReadOnlyList<PackingListSourceEnrichment>> GetSourceEnrichmentsAsync(IReadOnlyCollection<Guid> grossDemandIds, CancellationToken token = default);
}
