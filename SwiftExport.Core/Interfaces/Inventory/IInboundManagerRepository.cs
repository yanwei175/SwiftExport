using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Core.Interfaces.Inventory;

/// <summary>入库管理及来源查询持久化边界；不提供余额或流水写入。</summary>
public interface IInboundManagerRepository
{
    /// <summary>读取原单及相关反向、替代流水，异常事实不能被过滤掩盖。</summary>
    Task<IReadOnlyList<InventoryLedgerEntry>> ReadReverseLedgersAsync(Guid uid, CancellationToken token);
    /// <summary>只读读取原余额，含已删除投影以显式阻断异常。</summary>
    Task<InventoryBalance?> ReadReverseBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token);
    /// <summary>历史反向允许停用仓，但不允许缺失或已删除仓库。</summary>
    Task<bool> ReverseWarehouseExistsAsync(Guid warehouseId, CancellationToken token);
    /// <summary>版本保护下记录整单反过账状态及审计，不更改原始数量。</summary>
    Task MarkReversedAsync(InboundDocument header, CancellationToken token);
    /// <summary>读取采购来源过账校验投影及正式净额；不要求需求关联。</summary>
    Task<IReadOnlyList<InboundPurchasePostingSourceDto>> ReadPostingSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token);
    /// <summary>按稳定顺序先锁采购头再锁采购行，必须在入库头锁之前调用。</summary>
    Task LockPurchaseSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token);
    /// <summary>检查无法证明来源的正式历史，不能当作零收货。</summary>
    Task<bool> HasUnprovenReceiptAsync(CancellationToken token);
    /// <summary>只读取得同仓产品余额，不预占。</summary>
    Task<decimal> ReadBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token);
    /// <summary>只读校验仓库有效并返回当前名称；无效返回空。</summary>
    Task<string?> ReadActiveWarehouseNameAsync(Guid warehouseId, CancellationToken token);
    /// <summary>版本保护下更新正向过账状态，不修改原数量。</summary>
    Task MarkPostedAsync(InboundDocument header, CancellationToken token);
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
