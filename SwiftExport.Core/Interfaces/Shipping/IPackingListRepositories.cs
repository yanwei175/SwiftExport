using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Models.Shipping;

namespace SwiftExport.Core.Interfaces.Shipping;

public interface IPackingListDocumentRepository
{
    Task<PackingListDocument?> GetForUpdateAsync(Guid uid);
    Task<IReadOnlyList<PackingListContributionLine>> GetLinesForUpdateAsync(Guid uid);
    Task<PackingListDocument?> GetByCreateCommandForUpdateAsync(Guid commandId);
    Task<PackingListDocument?> GetByPostCommandForUpdateAsync(Guid commandId);
    Task<PackingListDocument?> GetByReverseCommandForUpdateAsync(Guid commandId);
    Task<int> InsertAsync(PackingListDocument document, IReadOnlyList<PackingListContributionLine> lines);
    Task<int> UpdateDraftAsync(PackingListDocument document, IReadOnlyList<PackingListContributionLine> lines, int expectedVersion);
    Task<int> SetVoidedAsync(Guid uid, int expectedVersion, string userName);
    Task<int> SetPostedAsync(Guid uid, int expectedVersion, Guid commandId, string previewToken, DateTime date, string userName);
    Task<int> SetReversedAsync(Guid uid, int expectedVersion, Guid commandId, string previewToken, DateTime date, string userName, string reason);
}

public interface IPackingListPlanningRepository
{
    Task<PagedResponseBase<PackingListSourceDto>> SearchSourcesAsync(PackingListSourceSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>以来源需求为主键加载 PL 草稿快照；仓库可由调用方显式选择，Allocation 仅作可选追溯。</summary>
    Task<PackingListSourceDto?> GetSourceForUpdateAsync(Guid grossDemandId, Guid? warehouseSiteId);
    Task<PackingListPlanningSnapshot?> LoadSnapshotAsync(Guid uid, bool forUpdate, CancellationToken cancellationToken = default);
}

public interface IPackingListQueryRepository
{
    Task<PagedResponseBase<PackingListListItemDto>> SearchAsync(PackingListSearchRequest request, CancellationToken cancellationToken = default);
    Task<PackingListDetailDto?> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<PackingListTraceDto?> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default);
}
