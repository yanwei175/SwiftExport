using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Interfaces.Inventory;

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

/// <summary>描述一条普通库存增减事实；快照缺省时由锁定主数据补齐。</summary>
public sealed class InventoryPostingItem
{
    public Guid CommandItemId { get; set; }
    public Guid WarehouseSiteId { get; set; }
    public Guid ProductId { get; set; }
    public string? SkuSnapshot { get; set; }
    public string? UnitSnapshot { get; set; }
    public string? ProductNameSnapshot { get; set; }
    public decimal QuantityDelta { get; set; }
    public InventoryTransactionTypeEnum TransactionType { get; set; }
    public string SourceDocumentType { get; set; } = string.Empty;
    public Guid? SourceDocumentId { get; set; }
    public Guid? SourceLineId { get; set; }
    public string? SourceDocumentNoSnapshot { get; set; }
    public Guid? ReversalOfLedgerId { get; set; }
    public Guid? ReplacementOfLedgerId { get; set; }
    public string? Reason { get; set; }
    public DateTime OccurredDate { get; set; }
}

/// <summary>描述一条完整反向请求；库存身份和数量不由调用方覆盖。</summary>
public sealed class InventoryReverseItem
{
    public Guid CommandItemId { get; set; }
    public Guid OriginalLedgerId { get; set; }
    public string SourceDocumentType { get; set; } = string.Empty;
    public Guid? SourceDocumentId { get; set; }
    public Guid? SourceLineId { get; set; }
    public string? SourceDocumentNoSnapshot { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime OccurredDate { get; set; }
}

/// <summary>描述原子纠错中的一对完整反向与替代事实。</summary>
public sealed class InventoryCorrectionItem
{
    public Guid OriginalLedgerId { get; set; }
    public Guid ReverseCommandItemId { get; set; }
    public string ReverseSourceDocumentType { get; set; } = string.Empty;
    public Guid? ReverseSourceDocumentId { get; set; }
    public Guid? ReverseSourceLineId { get; set; }
    public string? ReverseSourceDocumentNoSnapshot { get; set; }
    public DateTime ReverseOccurredDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public InventoryPostingItem Replacement { get; set; } = new();
}

/// <summary>返回原子纠错生成或重放的反向与替代流水结果。</summary>
public sealed class InventoryCorrectionResult
{
    public IReadOnlyList<InventoryPostingResultDto> Reversals { get; set; } = [];
    public IReadOnlyList<InventoryPostingResultDto> Replacements { get; set; } = [];
}
