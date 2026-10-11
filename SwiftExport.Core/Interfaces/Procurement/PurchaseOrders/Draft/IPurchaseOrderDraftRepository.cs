using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Contracts.Wrappers;

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
    /// <summary>查询全部未删除采购单的非空 BONO，借用来源用例事务。</summary>
    Task<bool> BoNoExistsAsync(string? boNo, CancellationToken cancellationToken = default);
    /// <summary>插入采购头并返回结构化拒绝；调用方必须检查失败并回滚。</summary>
    Task<RepositoryWriteResult> InsertPurchaseDraftAsync(PurchaseOrderHead head);
    Task<int> GetMaxLineNumberForUpdateAsync(Guid purchaseOrderId);
    Task<int> InsertPurchaseDraftLinesAsync(IEnumerable<PurchaseOrderLine> lines);
    Task<int> AdvanceDraftVersionAfterAppendAsync(Guid uid, int expectedVersion);
}
