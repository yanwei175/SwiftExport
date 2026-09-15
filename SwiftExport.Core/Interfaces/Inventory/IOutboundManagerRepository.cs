using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Core.Interfaces.Inventory;

/// <summary>出库管理及来源查询持久化边界；不提供余额或流水写入。</summary>
public interface IOutboundManagerRepository
{
    /// <summary>读取全部头行或幂等键关联流水及其反向、替代事实。</summary>
    Task<IReadOnlyList<InventoryLedgerEntry>> ReadReverseLedgersAsync(Guid uid, CancellationToken token);
    /// <summary>读取原投影和删除标记，不把缺失余额当零。</summary>
    Task<InventoryBalance?> ReadReverseBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token);
    /// <summary>历史反向要求仓库存在且未删除，不按停用状态阻断。</summary>
    Task<bool> ReverseWarehouseExistsAsync(Guid warehouseId, CancellationToken token);
    /// <summary>版本保护下保存反过账元数据，保留原过账信息和明细。</summary>
    Task MarkReversedAsync(OutboundDocument header, CancellationToken token);
    /// <summary>只读取得同仓产品余额，不预占。</summary>
    Task<decimal> ReadBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token);
    /// <summary>只读校验仓库有效并返回当前名称；无效返回空。</summary>
    Task<string?> ReadActiveWarehouseNameAsync(Guid warehouseId, CancellationToken token);
    /// <summary>版本保护下更新正向过账状态，不修改原数量。</summary>
    Task MarkPostedAsync(OutboundDocument header, CancellationToken token);
    /// <summary>分页查询单据摘要。</summary>
    Task<PagedResponseBase<OutboundManagerListItemDto>> SearchAsync(OutboundManagerSearchRequest request, CancellationToken token);
    /// <summary>读取完整单据；不存在返回空。</summary>
    Task<OutboundManagerDetailDto?> GetDetailAsync(Guid uid, CancellationToken token);
    /// <summary>按指定仓库分页读取正库存，排除本单产品。</summary>
    Task<PagedResponseBase<OutboundItemCandidateDto>> SearchItemsAsync(OutboundItemSearchRequest request, CancellationToken token);
    /// <summary>读取产品身份和单位，不以库存余额阻断草稿。</summary>
    Task<IReadOnlyList<OutboundItemCandidateDto>> GetProductsAsync(IReadOnlyList<Guid> ids, CancellationToken token);
    /// <summary>判断产品是否允许新建业务引用。</summary>
    Task<bool> IsProductActiveAsync(Guid productId, CancellationToken token);
    /// <summary>在当前事务中串行化幂等命令。</summary>
    Task<int> LockCommandAsync(Guid commandId, CancellationToken token);
    /// <summary>锁定单头，统一保护整张草稿。</summary>
    Task<OutboundDocument?> LockHeaderAsync(Guid uid, CancellationToken token);
    /// <summary>读取并锁定已有有效明细。</summary>
    Task<IReadOnlyList<OutboundDocumentLine>> LockLinesAsync(Guid uid, CancellationToken token);
    /// <summary>检查异常正式流水，防止状态损坏时删除正式事实。</summary>
    Task<bool> HasLedgerAsync(Guid uid, CancellationToken token);
    /// <summary>持久化已校验头行；expectedVersion 为零表示新增，否则按版本更新。</summary>
    Task SaveAsync(OutboundDocument header, IReadOnlyList<OutboundDocumentLine> lines, int expectedVersion, CancellationToken token);
    /// <summary>在调用方事务中软删整张单并递增版本。</summary>
    Task DeleteAsync(OutboundDocument header, CancellationToken token);
}
