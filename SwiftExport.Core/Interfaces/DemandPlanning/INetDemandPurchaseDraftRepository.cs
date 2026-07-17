using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning;

/// <summary>净需求采购草稿专用仓储；不提供旧采购 CRUD 能力。</summary>
public interface INetDemandPurchaseDraftRepository
{
    Task<NetDemandPurchaseDraftHead?> GetByExecutionScopeForUpdateAsync(
        Guid executionRequestId,
        string sourceType,
        Guid sourceUid,
        Guid supplierId);
    Task<IReadOnlyList<SafePurchaseDraftCandidateDto>> GetSafeAppendCandidatesAsync(
        string sourceType,
        Guid sourceUid,
        IEnumerable<Guid> supplierIds,
        CancellationToken cancellationToken = default);
    Task<NetDemandPurchaseDraftHead?> GetSafeDraftForUpdateAsync(Guid uid, int expectedVersion);
    Task<IReadOnlyList<NetDemandPurchaseDraftHead>> GetPurchaseDraftsByIdsAsync(
        IEnumerable<Guid> uids,
        CancellationToken cancellationToken = default);
    Task<int> InsertPurchaseDraftAsync(NetDemandPurchaseDraftHead head);
    Task<int> GetMaxLineNumberForUpdateAsync(Guid purchaseOrderId);
    Task<int> InsertPurchaseDraftLinesAsync(IEnumerable<NetDemandPurchaseDraftLine> lines);
    Task<int> RefreshDraftSummaryAfterAppendAsync(Guid uid, int expectedVersion);
    Task<NetDemandPurchaseDraftHead?> GetLifecycleDraftAsync(Guid uid, bool withUpdLock = false);
    Task<bool> HasDownstreamFactsAsync(Guid uid, bool withUpdLock = false);
    Task<IReadOnlyList<NetDemandPurchaseDraftLine>> GetDraftLinesForUpdateAsync(Guid purchaseOrderId);
    Task<int> VoidDraftAsync(Guid uid, int expectedVersion, string reason);
    Task<int> UpdateDraftLineQuantitiesAsync(IReadOnlyDictionary<Guid, decimal> quantities, string reason);
    Task<int> RefreshDraftSummaryAfterReleaseAsync(Guid uid, int expectedVersion);
}
