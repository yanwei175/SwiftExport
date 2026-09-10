using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Core.Entities.Procurement;

namespace SwiftExport.Core.Interfaces.Procurement;

/// <summary>采购草稿写入与生命周期专用仓储；由净需求转采购用例调用，不提供通用采购 CRUD。</summary>
public interface IPurchaseOrderDraftRepository
{
    Task<PurchaseOrderHead?> GetByExecutionScopeForUpdateAsync(
        Guid executionRequestId,
        string sourceType,
        Guid sourceUid,
        Guid supplierId);
    Task<IReadOnlyList<SafePurchaseDraftCandidateDto>> GetSafeAppendCandidatesAsync(
        string sourceType,
        Guid sourceUid,
        IEnumerable<Guid> supplierIds,
        CancellationToken cancellationToken = default);
    Task<PurchaseOrderHead?> GetSafeDraftForUpdateAsync(Guid uid, int expectedVersion);
    Task<IReadOnlyList<PurchaseOrderHead>> GetPurchaseDraftsByIdsAsync(
        IEnumerable<Guid> uids,
        CancellationToken cancellationToken = default);
    Task<int> InsertPurchaseDraftAsync(PurchaseOrderHead head);
    Task<int> GetMaxLineNumberForUpdateAsync(Guid purchaseOrderId);
    Task<int> InsertPurchaseDraftLinesAsync(IEnumerable<PurchaseOrderLine> lines);
    Task<int> AdvanceDraftVersionAfterAppendAsync(Guid uid, int expectedVersion);
    Task<PurchaseOrderHead?> GetLifecycleDraftAsync(Guid uid, bool withUpdLock = false);
    Task<bool> HasDownstreamFactsAsync(Guid uid, bool withUpdLock = false);
    Task<IReadOnlyList<PurchaseOrderLine>> GetDraftLinesForUpdateAsync(Guid purchaseOrderId);
    Task<int> VoidDraftAsync(Guid uid, int expectedVersion, string reason);
    Task<int> UpdateDraftLineQuantitiesAsync(IReadOnlyDictionary<Guid, decimal> quantities, string reason);
    Task<int> AdvanceDraftVersionAfterReleaseAsync(Guid uid, int expectedVersion);
}
