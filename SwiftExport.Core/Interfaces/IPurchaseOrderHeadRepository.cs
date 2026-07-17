using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 采购订单头仓储接口。
/// </summary>
public interface IPurchaseOrderHeadRepository : IRepository<PurchaseOrderHead>
{
    Task<PurchaseOrderHead?> GetByBoNoAsync(string boNo, bool withUpdLock = false);
    Task<PurchaseOrderHead?> GetByOrderNoAsync(string orderNo, bool withUpdLock = false);

    /// <summary>按执行请求、来源和供应商锁定幂等采购头。</summary>
    Task<PurchaseOrderHead?> GetByExecutionScopeForUpdateAsync(
        Guid executionRequestId,
        string sourceType,
        Guid sourceUid,
        Guid supplierId);

    /// <summary>读取可安全追加的活动未确认采购草稿。</summary>
    Task<IReadOnlyList<SafePurchaseDraftCandidateDto>> GetSafeAppendCandidatesAsync(
        string sourceType,
        Guid sourceUid,
        IEnumerable<Guid> supplierIds,
        CancellationToken cancellationToken = default);

    /// <summary>按 Uid 和期望版本锁定仍可安全追加的采购草稿。</summary>
    Task<PurchaseOrderHead?> GetSafeDraftForUpdateAsync(Guid uid, int expectedVersion);

    /// <summary>按采购头 Uid 批量回读创建或幂等命中的采购草稿。</summary>
    Task<IReadOnlyList<PurchaseOrderHead>> GetPurchaseDraftsByIdsAsync(
        IEnumerable<Guid> uids,
        CancellationToken cancellationToken = default);

    /// <summary>显式插入采购草稿，保留 Application 预生成的 Uid。</summary>
    Task<int> InsertPurchaseDraftAsync(PurchaseOrderHead head);

    /// <summary>追加行后按数据库事实刷新行数、总数量和版本。</summary>
    Task<int> RefreshDraftSummaryAfterAppendAsync(Guid uid, int expectedVersion);

    /// <summary>读取新净需求链路采购草稿；withUpdLock=true 时用于生命周期事务。</summary>
    Task<PurchaseOrderHead?> GetLifecycleDraftAsync(Guid uid, bool withUpdLock = false);

    /// <summary>判断采购草稿是否已有收货、库存或行收货事实。</summary>
    Task<bool> HasDownstreamFactsAsync(Guid uid, bool withUpdLock = false);

    /// <summary>安全取消采购草稿并推进版本。</summary>
    Task<int> VoidDraftAsync(Guid uid, int expectedVersion, string reason);

    /// <summary>减量后按行事实刷新采购头汇总并推进版本。</summary>
    Task<int> RefreshDraftSummaryAfterReleaseAsync(Guid uid, int expectedVersion);
}
