using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Core.Interfaces.Inventory;

/// <summary>入库管理及来源查询持久化边界；不提供余额或流水写入。</summary>
public interface IInboundManagerRepository
{
    /// <summary>分页查询单据摘要。</summary>
    Task<PagedResponseBase<InboundManagerListItemDto>> SearchAsync(InboundManagerSearchRequest request, CancellationToken token);
    /// <summary>读取完整单据；不存在返回空。</summary>
    Task<InboundManagerDetailDto?> GetDetailAsync(Guid uid, CancellationToken token);
    /// <summary>按采购行分页选源，不按仓库筛选、不加更新锁。</summary>
    Task<PagedResponseBase<InboundSourceCandidateDto>> SearchSourcesAsync(InboundSourceSearchRequest request, CancellationToken token);
    /// <summary>读取指定采购行快照；不校验来源版本或剩余额度。</summary>
    Task<IReadOnlyList<InboundSourceCandidateDto>> GetSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token);
    /// <summary>在当前事务中串行化幂等命令。</summary>
    Task<int> LockCommandAsync(Guid commandId, CancellationToken token);
    /// <summary>锁定单头，统一保护整张草稿。</summary>
    Task<InboundDocument?> LockHeaderAsync(Guid uid, CancellationToken token);
    /// <summary>读取并锁定已有有效明细。</summary>
    Task<IReadOnlyList<InboundDocumentLine>> LockLinesAsync(Guid uid, CancellationToken token);
    /// <summary>检查异常正式流水，防止状态损坏时删除正式事实。</summary>
    Task<bool> HasLedgerAsync(Guid uid, CancellationToken token);
    /// <summary>持久化已校验头行；expectedVersion 为零表示新增，否则按版本更新。</summary>
    Task SaveAsync(InboundDocument header, IReadOnlyList<InboundDocumentLine> lines, int expectedVersion, CancellationToken token);
    /// <summary>在调用方事务中软删整张单并递增版本。</summary>
    Task DeleteAsync(InboundDocument header, CancellationToken token);
}
