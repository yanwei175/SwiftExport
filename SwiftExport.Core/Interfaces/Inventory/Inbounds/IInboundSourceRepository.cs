using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Inventory.Inbounds;

/// <summary>入库采购来源查询、快照与事务内来源锁持久化边界。</summary>
public interface IInboundSourceRepository
{
    /// <summary>按采购行分页选源，不按仓库筛选、不加更新锁。</summary>
    Task<PagedResponseBase<InboundSourceCandidateDto>> SearchSourcesAsync(InboundSourceSearchRequest request, CancellationToken token);
    /// <summary>读取指定采购行快照；不校验来源版本或剩余额度。</summary>
    Task<IReadOnlyList<InboundSourceCandidateDto>> GetSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token);
    /// <summary>读取采购来源过账校验投影及正式净额；不要求需求关联。</summary>
    Task<IReadOnlyList<InboundPurchasePostingSourceDto>> ReadPostingSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token);
    /// <summary>按稳定顺序先锁采购头再锁采购行，必须在入库头锁之前调用。</summary>
    Task LockPurchaseSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token);
    /// <summary>检查无法证明来源的正式历史，不能当作零收货。</summary>
    Task<bool> HasUnprovenReceiptAsync(CancellationToken token);
}
