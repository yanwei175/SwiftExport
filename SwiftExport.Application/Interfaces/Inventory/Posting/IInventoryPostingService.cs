using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Interfaces.Inventory.Posting;

/// <summary>库存写入内核，仅限 Application 内部业务单据服务调用。</summary>
public interface IInventoryPostingService
{
    /// <summary>在调用方事务中追加普通库存事实；服务不提交事务，失败会使外层事务不可提交。</summary>
    Task<IReadOnlyList<InventoryPostingResultDto>> PostAsync(Guid commandId, IReadOnlyList<InventoryPostingItem> items, CancellationToken cancellationToken = default);

    /// <summary>按原流水追加完整反向事实；仓库、产品、单位和数量只从锁定原流水派生。</summary>
    Task<IReadOnlyList<InventoryPostingResultDto>> ReverseAsync(Guid commandId, IReadOnlyList<InventoryReverseItem> items, CancellationToken cancellationToken = default);

    /// <summary>在同一事务内完成原流水完整反向与替代事实，并按整组最终余额校验。</summary>
    Task<InventoryCorrectionResult> CorrectAsync(Guid commandId, IReadOnlyList<InventoryCorrectionItem> items, CancellationToken cancellationToken = default);
}

